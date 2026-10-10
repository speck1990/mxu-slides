import PresenterCore
import SwiftUI

struct SongSelectImportSheet: View {
    let model: AppModel
    let onImported: (String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var clientID = ProcessInfo.processInfo.environment["SLIDES_CCLI_CLIENT_ID"] ?? ""
    @State private var subscriptionKey = ProcessInfo.processInfo.environment["SLIDES_CCLI_SUBSCRIPTION_KEY"] ?? ""
    @State private var clientSecret = ProcessInfo.processInfo.environment["SLIDES_CCLI_CLIENT_SECRET"] ?? ""
    @State private var query = ""
    @State private var results: [SongSelectPayload.Result] = []
    @State private var client: SongSelectClient?
    @State private var busy = false
    @State private var status = "Enter CCLI partner credentials, then sign in."

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Import from SongSelect").font(.headline)
            TextField("CCLI partner client ID", text: $clientID)
            SecureField("CCLI partner subscription key", text: $subscriptionKey)
            SecureField("Client secret, if CCLI issued one", text: $clientSecret)
            Text("Register \(SongSelectClient.redirectURI) as the OAuth redirect URI with CCLI.")
                .font(.caption).foregroundStyle(.secondary)
            Button("Sign in with SongSelect") { Task { await signIn() } }
                .disabled(busy)
            HStack {
                TextField("Song title or CCLI song number", text: $query)
                    .onSubmit { Task { await search() } }
                Button("Search") { Task { await search() } }
                    .disabled(busy || client == nil || query.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            List(results) { song in
                HStack {
                    VStack(alignment: .leading) {
                        Text(song.title)
                        Text("CCLI \(song.songNumber) · \(song.authors.joined(separator: ", "))")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("Import") { Task { await importSong(song) } }
                        .disabled(busy || !song.canImport)
                }
            }
            .frame(minHeight: 230)
            Text(status).font(.callout).foregroundStyle(.secondary)
            HStack { Spacer(); Button("Close") { dismiss() } }
        }
        .padding(18)
        .frame(width: 560, height: 480)
    }

    private func signIn() async {
        busy = true
        defer { busy = false }
        do {
            let next = try SongSelectClient(clientID: clientID, subscriptionKey: subscriptionKey, clientSecret: clientSecret)
            status = "Complete sign-in in your browser…"
            try await next.signIn()
            client = next
            status = "Signed in. Search by title or CCLI song number."
        } catch { status = error.localizedDescription }
    }

    private func search() async {
        guard let client else { return }
        busy = true
        defer { busy = false }
        do {
            status = "Searching…"
            results = try await client.search(query)
            status = "\(results.count) songs found."
        } catch { status = error.localizedDescription }
    }

    private func importSong(_ song: SongSelectPayload.Result) async {
        guard let client else { return }
        busy = true
        defer { busy = false }
        do {
            status = "Importing…"
            let lyrics = try await client.lyrics(for: song.id)
            let id = try model.importSongSelect(lyrics)
            dismiss()
            onImported(id)
        } catch { status = error.localizedDescription }
    }
}
