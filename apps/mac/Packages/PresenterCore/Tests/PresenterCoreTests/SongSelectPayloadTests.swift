import Foundation
import Testing
@testable import PresenterCore

@Suite struct SongSelectPayloadTests {
    @Test func decodesSearchAndBuildsImportText() throws {
        let search = #"{"data":{"results":[{"id":"42","title":"Grace","songNumber":"12345","authors":[{"label":"Writer"}],"content":{"lyrics":{"exists":true,"isAuthorized":true}}}]}}"#
        let results = try JSONDecoder().decode(SongSelectPayload.SearchPage.self, from: Data(search.utf8)).data.results
        #expect(results.count == 1)
        #expect(results[0].canImport)
        #expect(results[0].songNumber == "12345")

        let response = #"{"data":{"title":"Grace","songNumber":"12345","authors":[{"label":"Writer"}],"copyrights":["© 2026 Publisher"],"lyricParts":[{"partLabel":"Verse 1","lyrics":"Line one\nLine two"},{"partLabel":"Chorus","lyrics":"Sing"}]}}"#
        let lyrics = try JSONDecoder().decode(SongSelectPayload.LyricsResponse.self, from: Data(response.utf8)).data
        let presentation = LyricTextImporter.makePresentation(try lyrics.importText())
        #expect(presentation.name == "Grace")
        #expect(presentation.ccli?.songNumber == 12345)
        #expect(presentation.slides.count == 2)
    }
}
