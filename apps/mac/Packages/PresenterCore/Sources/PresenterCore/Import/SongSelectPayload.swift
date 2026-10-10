import Foundation

public enum SongSelectPayload {
    public struct Result: Decodable, Identifiable, Sendable {
        public let id: String
        public let title: String
        public let songNumber: String
        public let authors: [String]
        public let canImport: Bool

        enum CodingKeys: String, CodingKey { case id, title, songNumber, authors, content }

        public init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            id = try container.decode(String.self, forKey: .id)
            title = try container.decode(String.self, forKey: .title)
            songNumber = try container.decodeIfPresent(String.self, forKey: .songNumber) ?? ""
            authors = try container.decodeIfPresent([Author].self, forKey: .authors)?.map(\.label) ?? []
            let content = try container.decodeIfPresent(Content.self, forKey: .content)
            canImport = content?.lyrics?.exists == true && content?.lyrics?.isAuthorized == true
        }
    }

    public struct SearchPage: Decodable, Sendable {
        public let data: Results
        public struct Results: Decodable, Sendable { public let results: [Result] }
    }

    public struct LyricsResponse: Decodable, Sendable {
        public let data: Lyrics
    }

    public struct Lyrics: Decodable, Sendable {
        public let title: String
        public let songNumber: String
        public let authors: [String]
        public let copyrights: [String]
        public let lyricParts: [Part]

        enum CodingKeys: String, CodingKey { case title, songNumber, authors, copyrights, lyricParts }

        public init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            title = try container.decode(String.self, forKey: .title)
            songNumber = try container.decodeIfPresent(String.self, forKey: .songNumber) ?? ""
            authors = try container.decodeIfPresent([Author].self, forKey: .authors)?.map(\.label) ?? []
            copyrights = try container.decodeIfPresent([String].self, forKey: .copyrights) ?? []
            lyricParts = try container.decodeIfPresent([Part].self, forKey: .lyricParts) ?? []
        }

        public func importText() throws -> String {
            guard !title.isEmpty, !songNumber.isEmpty else { throw ImportError.missingMetadata }
            let sections = lyricParts.compactMap { part -> String? in
                let body = part.lyrics.trimmingCharacters(in: .whitespacesAndNewlines)
                guard !body.isEmpty else { return nil }
                return "\(part.partLabel)\n\(body)"
            }
            guard !sections.isEmpty else { throw ImportError.noLyrics }
            return ([title] + sections + [
                "CCLI Song # \(songNumber)",
                authors.joined(separator: " | ")
            ] + copyrights).joined(separator: "\n\n")
        }

        public func makePresentation(themeId: String = "", linesPerSlide: Int = 2) throws -> Presentation {
            var presentation = LyricTextImporter.makePresentation(
                try importText(), themeId: themeId, linesPerSlide: linesPerSlide
            )
            var ccli = presentation.ccli ?? CCLIInfo()
            ccli.songNumber = Int(songNumber)
            ccli.songTitle = title
            ccli.author = authors.joined(separator: " | ")
            ccli.copyright = copyrights.joined(separator: "\n")
            presentation.ccli = ccli
            return presentation
        }
    }

    public struct Part: Decodable, Sendable {
        public let partLabel: String
        public let lyrics: String
    }

    private struct Author: Decodable, Sendable {
        let label: String
        init(from decoder: Decoder) throws {
            let value = try decoder.singleValueContainer()
            if let name = try? value.decode(String.self) { label = name }
            else {
                let author = try value.decode(AuthorObject.self)
                label = author.label ?? author.name ?? ""
            }
        }
    }
    private struct AuthorObject: Decodable, Sendable { let label: String?; let name: String? }
    private struct Content: Decodable, Sendable { let lyrics: LyricPermission? }
    private struct LyricPermission: Decodable, Sendable { let exists: Bool; let isAuthorized: Bool }
    public enum ImportError: LocalizedError {
        case noLyrics, missingMetadata
        public var errorDescription: String? {
            switch self {
            case .noLyrics: "This SongSelect song has no importable lyrics."
            case .missingMetadata: "SongSelect returned no song title or CCLI number."
            }
        }
    }
}
