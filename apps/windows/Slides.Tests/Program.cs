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
    ("library survives a new show and keeps show snapshots independent", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var libraryPath = Path.Combine(folder, "library.json");
        var showPath = Path.Combine(folder, "old.pshow");
        var newShowPath = Path.Combine(folder, "new.pshow");
        try
        {
            var song = SongParser.Parse("Shared song", "Verse 1\nOriginal lyrics");
            ShowFiles.Save(showPath, new Show { Presentations = [song], ServiceItems = [song.Id] });

            var library = LibraryFiles.Load(libraryPath);
            Check(library.ImportMissing(ShowFiles.Load(showPath).Presentations), "existing show imported into library");
            Check(!library.ImportMissing(ShowFiles.Load(showPath).Presentations) && library.Presentations.Count == 1,
                "opening the same show does not duplicate the song");
            LibraryFiles.Save(libraryPath, library);

            var nextShow = new Show { ServiceItems = [] };
            library = LibraryFiles.Load(libraryPath);
            Check(nextShow.ServiceItems!.Count == 0 && library.Presentations.Count == 1, "new show keeps library");
            library.AddToShow(nextShow, song.Id);
            library.AddToShow(nextShow, song.Id);
            Check(nextShow.ServiceItems!.SequenceEqual([song.Id, song.Id]) && nextShow.Presentations.Count == 1,
                "library song can appear twice in the service");
            nextShow.Presentations[0].Slides[0].Text = "Updated lyrics";
            Check(library.Presentations[0].Slides[0].Text == "Original lyrics", "editing show copy does not mutate library silently");
            library.Upsert(nextShow.Presentations[0]);
            LibraryFiles.Save(libraryPath, library);
            ShowFiles.Save(newShowPath, nextShow);
            Check(LibraryFiles.Load(libraryPath).Presentations[0].Slides[0].Text == "Updated lyrics",
                "explicit song edit persists in library");
            Check(!library.ImportMissing(ShowFiles.Load(showPath).Presentations) &&
                library.Presentations[0].Slides[0].Text == "Updated lyrics",
                "opening an older show does not overwrite the library edit");
            Check(ShowFiles.Load(showPath).Presentations[0].Slides[0].Text == "Original lyrics",
                "older show keeps its embedded snapshot");
            Check(ShowFiles.Load(newShowPath).ServiceItems!.Count == 2, "new show remains self-contained");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }),
    ("folders organize the library without changing show snapshots", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var song = SongParser.Parse("Folder song", "Lyrics");
            var library = new PresentationLibrary();
            library.Upsert(song);
            var folder = library.CreateFolder(" Worship ");
            library.MoveToFolder(song.Id, folder.Id);
            LibraryFiles.Save(path, library);

            var reopened = LibraryFiles.Load(path);
            Check(reopened.Folders[0].Name == "Worship" && reopened.FolderFor(song.Id) == folder.Id,
                "folder placement survives restart");
            reopened.RenameFolder(folder.Id, "Songs");
            try { reopened.CreateFolder("songs"); throw new Exception("FAIL duplicate folder accepted"); }
            catch (ArgumentException) { }
            var show = new Show { ServiceItems = [] };
            reopened.AddToShow(show, song.Id);
            reopened.DeleteFolder(folder.Id);
            Check(reopened.FolderFor(song.Id) is null && reopened.Presentations.Count == 1,
                "deleting a folder keeps its library items");
            Check(show.Presentations.Count == 1 && show.ServiceItems!.Count == 1,
                "folder changes do not alter show content");

            File.WriteAllText(path, """{"version":1,"presentations":[]}""");
            Check(LibraryFiles.Load(path).Folders.Count == 0, "earlier library files open without folders");
        }
        finally { File.Delete(path); }
    }),
    ("media relinking repairs unique matches and leaves ambiguous names alone", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var media = Path.Combine(folder, "media");
            var other = Path.Combine(folder, "other");
            Directory.CreateDirectory(media);
            Directory.CreateDirectory(other);
            var unique = Path.Combine(media, "screen.png");
            File.WriteAllText(unique, "image");
            File.WriteAllText(Path.Combine(media, "duplicate.png"), "first");
            File.WriteAllText(Path.Combine(other, "duplicate.png"), "second");
            var oldRoot = Path.Combine(folder, "old");
            var presentation = new Presentation { Slides =
            [
                new Slide { MediaPath = Path.Combine(oldRoot, "screen.png") },
                new Slide { MediaPath = Path.Combine(oldRoot, "duplicate.png") },
                new Slide { MediaPath = Path.Combine(oldRoot, "missing.png") },
                new Slide { MediaPath = unique }
            ] };
            var result = MediaRelinker.Relink([presentation], folder);
            Check(result == new MediaRelinkResult(1, 1, 1), "relink counts");
            Check(presentation.Slides[0].MediaPath == unique, "unique file relinked");
            Check(presentation.Slides[1].MediaPath == Path.Combine(oldRoot, "duplicate.png"),
                "ambiguous file remains unchanged");
            Check(presentation.Slides[3].MediaPath == unique, "working file remains unchanged");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
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
