using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Slides.Core;

namespace Slides;

internal sealed class SongSelectClient : IDisposable
{
    internal const string RedirectUri = "http://127.0.0.1:8765/callback/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _clientId;
    private readonly string _subscriptionKey;
    private readonly string _clientSecret;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;

    internal SongSelectClient(string clientId, string subscriptionKey, string clientSecret = "")
    {
        _clientId = clientId.Trim();
        _subscriptionKey = subscriptionKey.Trim();
        _clientSecret = clientSecret.Trim();
        if (_clientId.Length == 0 || _subscriptionKey.Length == 0)
            throw new ArgumentException("Enter the CCLI partner client ID and subscription key.");
    }

    internal async Task SignInAsync(CancellationToken cancellationToken)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var query = Form(new Dictionary<string, string>
        {
            ["client_id"] = _clientId, ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code", ["scope"] = "openid cclipartnerapi.read offline_access",
            ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
        });
        using var listener = new TcpListener(IPAddress.Loopback, 8765);
        listener.Start();
        Process.Start(new ProcessStartInfo("https://identityservices.ccli.com/connect/authorize?" + query)
        { UseShellExecute = true });
        using var connection = await listener.AcceptTcpClientAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        var target = requestLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
        if (target is null || !target.StartsWith("/callback/", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected SongSelect callback path.");
        var callback = new Uri("http://127.0.0.1:8765" + target);
        var parameters = callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]),
                part => part.Length > 1 ? Uri.UnescapeDataString(part[1].Replace('+', ' ')) : "");
        var body = "SongSelect sign-in complete. Return to Slides.";
        var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(response, cancellationToken);
        if (!parameters.TryGetValue("state", out var returnedState) || returnedState != state)
            throw new InvalidOperationException("SongSelect sign-in state did not match.");
        if (parameters.TryGetValue("error", out var error)) throw new InvalidOperationException("SongSelect sign-in failed: " + error);
        if (!parameters.TryGetValue("code", out var code)) throw new InvalidOperationException("SongSelect returned no sign-in code.");
        await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = RedirectUri, ["client_id"] = _clientId, ["code_verifier"] = verifier
        }, cancellationToken);
    }

    internal async Task<IReadOnlyList<SongSelectResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var path = "/songs?" + Form(new Dictionary<string, string>
        {
            ["filter[search]"] = query.Trim(), ["page[number]"] = "1", ["page[size]"] = "25",
            ["filter[musthavelyrics]"] = "true"
        });
        return SongSelect.ReadResults(await GetAsync(path, cancellationToken));
    }

    internal async Task<Presentation> ImportAsync(string id, CancellationToken cancellationToken) =>
        SongSelect.ReadLyrics(await GetAsync("/songs/" + Uri.EscapeDataString(id) + "/lyrics", cancellationToken));

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        await RefreshIfNeededAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, SongSelect.ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        using var response = await Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("SongSelect sign-in expired. Sign in again.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Your SongSelect account is not licensed for this song.");
        if ((int)response.StatusCode == 429)
            throw new InvalidOperationException("SongSelect is rate limiting requests. Try again shortly.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task RefreshIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-1)) return;
        if (_refreshToken is null) throw new InvalidOperationException("Sign in to SongSelect first.");
        await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = _refreshToken, ["client_id"] = _clientId
        }, cancellationToken);
    }

    private async Task TokenAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        if (_clientSecret.Length > 0) fields["client_secret"] = _clientSecret;
        using var response = await Http.PostAsync("https://identityservices.ccli.com/connect/token",
            new FormUrlEncodedContent(fields), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SongSelect token request failed ({(int)response.StatusCode}). Check CCLI partner access and redirect URI.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        _accessToken = root.GetProperty("access_token").GetString();
        if (string.IsNullOrEmpty(_accessToken)) throw new InvalidOperationException("SongSelect returned no access token.");
        if (root.TryGetProperty("refresh_token", out var refresh)) _refreshToken = refresh.GetString();
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var expiry)
            && expiry.TryGetInt32(out var seconds) ? seconds : 3600);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Form(Dictionary<string, string> fields) => string.Join("&", fields.Select(pair =>
        Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

    public void Dispose() { _accessToken = null; _refreshToken = null; }
}
