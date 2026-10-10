using System.Text.Json;
using Slides.Core;

var cases = new (string Name, Action Run)[]
{
    ("song sections and blank lines", () =>
    {
        var song = SongParser.Parse(" Amazing Grace ", "[Verse]\r\nAmazing grace\r\nHow sweet\r\n\r\n[Chorus]\r\nSing praise");
        Check(song.Name == "Amazing Grace" && song.Slides.Count == 2, "song structure");
        Check(song.Slides[0].Text == "Amazing grace\nHow sweet", "verse text");
        Check(song.Slides[1].Name == "Chorus", "section heading");
    }),
    ("bare song headings label slides instead of appearing in lyrics", () =>
    {
        var song = SongParser.Parse("A song", "Verse 1\nThis is verse 1\n\nChorus\nChorus singing\n\nVerse 2\nAnother verse");
        Check(song.Slides.Select(slide => slide.Name).SequenceEqual(["Verse 1", "Chorus", "Verse 2"]), "bare section labels");
        Check(song.Slides.Select(slide => slide.Text).SequenceEqual(["This is verse 1", "Chorus singing", "Another verse"]), "headings removed from lyrics");
        Check(SongParser.Parse("A song", SongParser.ToEditableLyrics(song)).Slides.Select(slide => slide.Name)
            .SequenceEqual(song.Slides.Select(slide => slide.Name)), "editing preserves section labels");
        Check(SongParser.Parse("A song", "Bridge over troubled water").Slides[0].Text == "Bridge over troubled water", "lyrics are not mistaken for headings");
    }),
    ("old Windows song headings are repaired when a show opens", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            File.WriteAllText(path, """{"version":1,"presentations":[{"id":"a","name":"A song","kind":"song","slides":[{"name":"Verse 1","text":"Verse 1\nThis is verse 1"},{"name":"Verse 2","text":"Chorus\nChorus singing"},{"name":"Verse 3","text":"Verse 2\nAnother verse"}]}]}""");
            var song = ShowFiles.Load(path).Presentations[0];
            Check(song.Slides.Select(slide => slide.Name).SequenceEqual(["Verse 1", "Chorus", "Verse 2"]), "old labels repaired");
            Check(song.Slides[1].Text == "Chorus singing", "old heading removed");
        }
        finally { File.Delete(path); }
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
    ("legacy shows become a service with a separate library", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            File.WriteAllText(path, """{"version":1,"presentations":[{"id":"a","name":"First","kind":"song","slides":[]},{"id":"b","name":"Second","kind":"song","slides":[]}]}""");
            var show = ShowFiles.Load(path);
            Check(show.ServiceItems!.SequenceEqual(["a", "b"]), "legacy service order");
            show.RemoveFromService(0);
            Check(show.Presentations.Count == 2 && show.ServiceItems!.SequenceEqual(["b"]), "removal keeps library");
            show.AddToService("a");
            show.AddToService("a");
            show.MoveServiceItem(2, 0);
            ShowFiles.Save(path, show);
            Check(ShowFiles.Load(path).ServiceItems!.SequenceEqual(["a", "b", "a"]), "service order survives save");
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
