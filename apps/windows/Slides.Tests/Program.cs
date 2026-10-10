using System.Text.Json;
using Slides.Core;

var cases = new (string Name, Action Run)[]
{
    ("SongSelect search and lyric import", () =>
    {
        var results = SongSelect.ReadResults("""{"data":{"results":[{"id":"42","title":"Grace","songNumber":"12345","authors":[{"label":"Writer"}],"content":{"lyrics":{"exists":true,"isAuthorized":true}}}]}}""");
        Check(results.Count == 1 && results[0].CanImport && results[0].SongNumber == "12345", "search result");
        var song = SongSelect.ReadLyrics("""{"data":{"title":"Grace","songNumber":"12345","copyrights":["© 2026 Publisher"],"lyricParts":[{"partLabel":"Verse 1","lyrics":"Line one\nLine two"},{"partLabel":"Chorus","lyrics":"Sing"}]}}""");
        Check(song.CcliSongNumber == 12345 && song.Slides.Count == 2, "CCLI and sections");
        Check(song.Slides[0].Text == "Line one\nLine two" && song.Copyright == "© 2026 Publisher", "lyrics and copyright");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            ShowFiles.Save(path, new Show { Presentations = [song] });
            Check(ShowFiles.Load(path).Presentations[0].CcliSongNumber == 12345, "CCLI metadata round trip");
        }
        finally { File.Delete(path); }
    }),
    ("song sections and blank lines", () =>
    {
        var song = SongParser.Parse(" Amazing Grace ", "[Verse]\r\nAmazing grace\r\nHow sweet\r\n\r\n[Chorus]\r\nSing praise");
        Check(song.Name == "Amazing Grace" && song.Slides.Count == 2, "song structure");
        Check(song.Slides[0].Text == "Amazing grace\nHow sweet", "verse text");
        Check(song.Slides[1].Name == "Chorus 1", "section heading");
    }),
    ("show persists offline", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            ShowFiles.Save(path, new Show { Presentations = [SongParser.Parse("Test", "Line one")] });
            Check(ShowFiles.Load(path).Presentations[0].Slides[0].Text == "Line one", "show round trip");
        }
        finally { File.Delete(path); }
    }),
    ("MxU presentation subset uses shared schema", () =>
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "documents.json")));
        var types = schema.RootElement.GetProperty("types");
        Check(types.TryGetProperty("Presentation", out _) && types.TryGetProperty("SlideObject", out _), "shared schema");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"name":"Song","presentationKind":"song","slides":[{"name":"Verse","backgroundFill":{"colorHex":"#123456FF"},"objects":[{"objectKind":"text","text":"Hello"},{"objectKind":"text","text":"Hidden","hidden":true},{"objectKind":"shape","text":"Ignored"}]}]}""");
            var imported = MxuPresentationImport.Load(path);
            Check(imported.Slides[0].Text == "Hello", "visible lyrics");
            Check(imported.Slides[0].Background == "#123456FF", "background color");
        }
        finally { File.Delete(path); }
    })
};

foreach (var test in cases)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void Check(bool result, string name)
{
    if (!result) throw new Exception($"FAIL {name}");
}
