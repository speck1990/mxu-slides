import AppKit
import CryptoKit
import Foundation
import Network
import PresenterCore

@MainActor final class SongSelectClient {
    static let redirectURI = "http://127.0.0.1:8765/callback/"
    private let clientID: String
    private let subscriptionKey: String
    private let clientSecret: String
    private var accessToken: String?
    private var refreshToken: String?
    private var expiresAt = Date.distantPast

    init(clientID: String, subscriptionKey: String, clientSecret: String = "") throws {
        self.clientID = clientID.trimmingCharacters(in: .whitespacesAndNewlines)
        self.subscriptionKey = subscriptionKey.trimmingCharacters(in: .whitespacesAndNewlines)
        self.clientSecret = clientSecret.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !self.clientID.isEmpty, !self.subscriptionKey.isEmpty else {
            throw SongSelectError.message("Enter the CCLI partner client ID and subscription key.")
        }
    }

    func signIn() async throws {
        let verifier = Data((0..<32).map { _ in UInt8.random(in: .min ... .max) }).base64URL
        let challenge = Data(SHA256.hash(Data(verifier.utf8))).base64URL
        let state = UUID().uuidString
        var components = URLComponents(string: "https://identityservices.ccli.com/connect/authorize")!
        components.queryItems = [
            .init(name: "client_id", value: clientID), .init(name: "redirect_uri", value: Self.redirectURI),
            .init(name: "response_type", value: "code"),
            .init(name: "scope", value: "openid cclipartnerapi.read offline_access"),
            .init(name: "state", value: state), .init(name: "code_challenge", value: challenge),
            .init(name: "code_challenge_method", value: "S256")
        ]
        let callback = try await SongSelectLoopback().authorize(components.url!)
        let items = URLComponents(url: callback, resolvingAgainstBaseURL: false)?.queryItems ?? []
        func item(_ name: String) -> String? { items.first(where: { $0.name == name })?.value }
        guard item("state") == state else { throw SongSelectError.message("SongSelect sign-in state did not match.") }
        if let error = item("error") { throw SongSelectError.message("SongSelect sign-in failed: \(error)") }
        guard let code = item("code") else { throw SongSelectError.message("SongSelect returned no sign-in code.") }
        try await requestToken([
            "grant_type": "authorization_code", "code": code, "client_id": clientID,
            "redirect_uri": Self.redirectURI, "code_verifier": verifier
        ])
    }

    func search(_ query: String) async throws -> [SongSelectPayload.Result] {
        guard !query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return [] }
        var components = URLComponents(string: "https://api.ccli.com/ss/v2/songs")!
        components.queryItems = [
            .init(name: "filter[search]", value: query), .init(name: "page[number]", value: "1"),
            .init(name: "page[size]", value: "25"), .init(name: "filter[musthavelyrics]", value: "true")
        ]
        let data = try await get(components.url!)
        return try JSONDecoder().decode(SongSelectPayload.SearchPage.self, from: data).data.results
    }

    func lyrics(for id: String) async throws -> SongSelectPayload.Lyrics {
        guard let encoded = id.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed),
              let url = URL(string: "https://api.ccli.com/ss/v2/songs/\(encoded)/lyrics") else {
            throw SongSelectError.message("Invalid SongSelect song ID.")
        }
        let data = try await get(url)
        return try JSONDecoder().decode(SongSelectPayload.LyricsResponse.self, from: data).data
    }

    private func get(_ url: URL) async throws -> Data {
        if Date() >= expiresAt.addingTimeInterval(-60) {
            guard let refreshToken else { throw SongSelectError.message("Sign in to SongSelect first.") }
            try await requestToken(["grant_type": "refresh_token", "refresh_token": refreshToken, "client_id": clientID])
        }
        var request = URLRequest(url: url)
        request.setValue("Bearer \(accessToken ?? "")", forHTTPHeaderField: "Authorization")
        request.setValue(subscriptionKey, forHTTPHeaderField: "Ocp-Apim-Subscription-Key")
        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        switch status {
        case 200..<300: return data
        case 401: throw SongSelectError.message("SongSelect sign-in expired. Sign in again.")
        case 403: throw SongSelectError.message("Your SongSelect account is not licensed for this song.")
        case 429: throw SongSelectError.message("SongSelect is rate limiting requests. Try again shortly.")
        default: throw SongSelectError.message("SongSelect request failed (\(status)).")
        }
    }

    private func requestToken(_ fields: [String: String]) async throws {
        var request = URLRequest(url: URL(string: "https://identityservices.ccli.com/connect/token")!)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        var components = URLComponents()
        var body = fields
        if !clientSecret.isEmpty { body["client_secret"] = clientSecret }
        components.queryItems = body.map { URLQueryItem(name: $0.key, value: $0.value) }
        request.httpBody = components.percentEncodedQuery?.data(using: .utf8)
        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else {
            throw SongSelectError.message("SongSelect token request failed (\(status)). Check CCLI partner access and redirect URI.")
        }
        let token = try JSONDecoder().decode(Token.self, from: data)
        guard !token.access_token.isEmpty else { throw SongSelectError.message("SongSelect returned no access token.") }
        accessToken = token.access_token
        refreshToken = token.refresh_token ?? refreshToken
        expiresAt = Date().addingTimeInterval(TimeInterval(token.expires_in ?? 3600))
    }

    private struct Token: Decodable { let access_token: String; let refresh_token: String?; let expires_in: Int? }
}

private enum SongSelectError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let text) = self { return text }; return nil }
}

private extension Data {
    var base64URL: String { base64EncodedString().replacingOccurrences(of: "+", with: "-")
        .replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "") }
}

private final class SongSelectLoopback: @unchecked Sendable {
    private let queue = DispatchQueue(label: "slides.songselect.callback")
    private var listener: NWListener?
    private var continuation: CheckedContinuation<URL, Error>?
    private var completed = false

    func authorize(_ browserURL: URL) async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            self.continuation = continuation
            do {
                let port = NWEndpoint.Port(rawValue: 8765)!
                let parameters = NWParameters.tcp
                parameters.requiredLocalEndpoint = .hostPort(host: .ipv4(.loopback), port: port)
                let listener = try NWListener(using: parameters, on: port)
                self.listener = listener
                listener.stateUpdateHandler = { [self] state in
                    switch state {
                    case .ready: Task { @MainActor in NSWorkspace.shared.open(browserURL) }
                    case .failed(let error): finish(.failure(error))
                    default: break
                    }
                }
                listener.newConnectionHandler = { [self] connection in
                    connection.start(queue: queue)
                    connection.receive(minimumIncompleteLength: 1, maximumLength: 8192) { data, _, _, error in
                        guard error == nil, let data, let request = String(data: data, encoding: .utf8),
                              let target = request.components(separatedBy: " ").dropFirst().first,
                              target.hasPrefix("/callback/"),
                              let url = URL(string: "http://127.0.0.1:8765\(target)") else {
                            connection.cancel()
                            return
                        }
                        let body = "SongSelect sign-in complete. Return to Slides."
                        let response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: \(body.utf8.count)\r\nConnection: close\r\n\r\n\(body)"
                        connection.send(content: Data(response.utf8), completion: .contentProcessed { _ in connection.cancel() })
                        finish(.success(url))
                    }
                }
                listener.start(queue: queue)
                queue.asyncAfter(deadline: .now() + 300) { [self] in
                    finish(.failure(SongSelectError.message("SongSelect sign-in timed out.")))
                }
            } catch { finish(.failure(error)) }
        }
    }

    private func finish(_ result: Result<URL, Error>) {
        guard !completed else { return }
        completed = true
        listener?.cancel()
        continuation?.resume(with: result)
        continuation = nil
    }
}
