using System.IO.Compression;
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
    ("slide editor keeps source untouched and persists deck changes", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var media = Path.Combine(folder, "background.png");
            File.WriteAllBytes(media, [1, 2, 3]);
            var original = PresentationEditing.NewDeck();
            var draft = PresentationEditing.Copy(original);
            draft.Name = "Sunday welcome";
            draft.Slides[0].Text = "Welcome";
            draft.Slides[0].Background = "#123456FF";
            var mediaSlide = PresentationEditing.AddSlide(draft, media);
            mediaSlide.Text = "Join us";
            var duplicate = PresentationEditing.DuplicateSlide(draft, 0);
            Check(duplicate.Id != draft.Slides[0].Id && duplicate.Text == "Welcome", "duplicate is independent");
            PresentationEditing.MoveSlide(draft, 2, 0);
            draft.Slides.RemoveAt(1);
            PresentationEditing.Validate(draft);
            Check(original.Slides.Count == 1 && original.Slides[0].Text == "", "cancel can discard draft");
            Check(draft.Slides.Select(slide => slide.Id).SequenceEqual([mediaSlide.Id, duplicate.Id]), "slide order and removal");

            var show = new Show { Presentations = [draft], ServiceItems = [draft.Id, draft.Id] };
            var path = Path.Combine(folder, "show.pshow");
            ShowFiles.Save(path, show);
            var reopened = ShowFiles.Load(path);
            Check(reopened.ServiceItems!.Count == 2 && reopened.Presentations[0].Slides[0].MediaPath == media,
                "edited deck and repeated service references persist");
            Check(reopened.Presentations[0].Slides[1].Background == "#123456FF", "background persists");
            var library = new PresentationLibrary();
            library.Upsert(draft);
            Check(library.Presentations[0].Slides.Count == 2, "edited deck can update library");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }),
    ("slide editor rejects invalid or missing slide content", () =>
    {
        var deck = PresentationEditing.NewDeck();
        deck.Slides[0].Background = "blue";
        try { PresentationEditing.Validate(deck); throw new Exception("FAIL invalid color accepted"); }
        catch (ArgumentException) { }
        deck.Slides[0].Background = "#123456";
        deck.Slides[0].MediaPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try { PresentationEditing.Validate(deck); throw new Exception("FAIL missing media accepted"); }
        catch (FileNotFoundException) { }
        deck.Slides[0].MediaPath = null;
        deck.Slides.Clear();
        try { PresentationEditing.Validate(deck); throw new Exception("FAIL empty deck accepted"); }
        catch (ArgumentException) { }
    }),
    ("starter looks and typography persist without changing older shows", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            var song = SongParser.Parse("Theme song", "Verse 1\nSing together\n\nChorus\nSing again");
            Check(song.Slides.All(slide => slide.TextStyle is null), "legacy songs use default typography");
            var old = PresentationEditing.Copy(song);
            var editorial = SlideThemes.All.Single(theme => theme.Id == "editorialSerif");
            SlideThemes.Apply(song, editorial);
            Check(song.Slides.All(slide => slide.Background == "#14120FFF" &&
                slide.TextStyle?.FontName == "Georgia" && slide.TextStyle.ColorHex == "#F7F3EAFF"),
                "Mac starter palette applied to every slide");
            song.Slides[0].TextStyle!.FontSize = 62;
            Check(song.Slides[1].TextStyle!.FontSize == 50, "each slide owns its own style");
            var added = PresentationEditing.AddSlide(song);
            Check(added.TextStyle?.FontName == "Georgia" && added.Background == "#14120FFF",
                "new slide inherits the current look");
            added.TextStyle!.ColorHex = "#123456";
            PresentationEditing.Validate(song);
            var edited = SongParser.Parse(song.Name, SongParser.ToEditableLyrics(song));
            PresentationEditing.PreserveSongDesign(song, edited);
            Check(edited.Slides[0].Id == song.Slides[0].Id &&
                edited.Slides[0].TextStyle?.FontSize == 62, "lyric edits retain slide identity and typography");
            Check(old.Slides[0].TextStyle is null, "unmodified song remains legacy compatible");
            ShowFiles.Save(path, new Show { Presentations = [song] });
            var reopened = ShowFiles.Load(path).Presentations[0];
            Check(reopened.Slides[0].TextStyle?.FontSize == 62 &&
                reopened.Slides[2].TextStyle?.ColorHex == "#123456", "styles survive show save");
        }
        finally { File.Delete(path); }
    }),
    ("typography rejects invalid size and color", () =>
    {
        var deck = PresentationEditing.NewDeck();
        deck.Slides[0].TextStyle = new SlideTextStyle { FontSize = double.NaN };
        try { PresentationEditing.Validate(deck); throw new Exception("FAIL NaN size accepted"); }
        catch (ArgumentException) { }
        deck.Slides[0].TextStyle!.FontSize = 54;
        deck.Slides[0].TextStyle!.ColorHex = "red";
        try { PresentationEditing.Validate(deck); throw new Exception("FAIL invalid text color accepted"); }
        catch (ArgumentException) { }
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
    ("portable show opens with media after the originals are removed", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var image = Path.Combine(folder, "background.png");
            var video = Path.Combine(folder, "motion.mp4");
            var package = Path.Combine(folder, "service.slidespack");
            File.WriteAllBytes(image, [1, 2, 3, 4]);
            File.WriteAllBytes(video, [5, 6, 7]);
            var presentation = new Presentation { Name = "Media", Slides =
            [
                new Slide { Name = "Image", MediaPath = image },
                new Slide { Name = "Video", MediaPath = video },
                new Slide { Name = "Image again", MediaPath = image }
            ] };
            var show = new Show { Presentations = [presentation], ServiceItems = [presentation.Id, presentation.Id] };
            ShowPackage.Save(package, show);
            Check(show.Presentations[0].Slides[0].MediaPath == image, "packing does not change the open show");
            using (var archive = ZipFile.OpenRead(package))
            {
                Check(archive.Entries.Count(entry => entry.FullName.StartsWith("media/")) == 2,
                    "reused media is packed once");
                using var reader = new StreamReader(archive.GetEntry("show.json")!.Open());
                Check(!reader.ReadToEnd().Contains(image, StringComparison.Ordinal), "package omits original absolute paths");
            }
            File.Delete(image);
            File.Delete(video);
            var opened = ShowPackage.Open(package, Path.Combine(folder, "cache"));
            Check(opened.ServiceItems!.SequenceEqual([presentation.Id, presentation.Id]), "service order survives packaging");
            Check(File.ReadAllBytes(opened.Presentations[0].Slides[0].MediaPath!).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "image works without its original path");
            Check(File.ReadAllBytes(opened.Presentations[0].Slides[1].MediaPath!).SequenceEqual(new byte[] { 5, 6, 7 }),
                "video works without its original path");
            Check(opened.Presentations[0].Slides[0].MediaPath == opened.Presentations[0].Slides[2].MediaPath,
                "repeated media uses one cached copy");
            var library = new PresentationLibrary();
            library.ImportMissing(show.Presentations);
            library.Presentations[0].Name = "My edited library title";
            Check(library.RepairMissingMediaFrom(opened.Presentations), "package repairs stale library media paths");
            Check(library.Presentations[0].Slides[0].MediaPath == opened.Presentations[0].Slides[0].MediaPath &&
                library.Presentations[0].Name == "My edited library title", "repair preserves library edits");
            Check(ShowPackage.Open(package, Path.Combine(folder, "cache")).Presentations[0].Slides[0].MediaPath ==
                opened.Presentations[0].Slides[0].MediaPath, "reopening uses the same verified cache");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }),
    ("portable shows reject missing media and unsafe archive references", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var package = Path.Combine(folder, "unsafe.slidespack");
        try
        {
            Directory.CreateDirectory(folder);
            var show = new Show { Presentations = [new Presentation { Slides =
                [new Slide { MediaPath = Path.Combine(folder, "missing.png") }] }] };
            try { ShowPackage.Save(package, show); throw new Exception("FAIL missing media packed"); }
            catch (FileNotFoundException) { }
            Check(!File.Exists(package), "failed package does not replace a show");

            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("format.txt").Open()))
                    writer.Write("Slides portable show 1\n");
                using (var writer = new StreamWriter(archive.CreateEntry("show.json").Open()))
                    writer.Write("""{"version":1,"presentations":[{"slides":[{"mediaPath":"../../escape.png"}]}]}""");
            }
            try { ShowPackage.Open(package, Path.Combine(folder, "cache")); throw new Exception("FAIL unsafe path accepted"); }
            catch (InvalidDataException) { }
            Check(!Directory.Exists(Path.Combine(folder, "cache")), "unsafe archive extracts nothing");

            File.Delete(package);
            var badMedia = "media/" + new string('0', 64) + ".png";
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("format.txt").Open()))
                    writer.Write("Slides portable show 1\n");
                using (var writer = new StreamWriter(archive.CreateEntry("show.json").Open()))
                    writer.Write("{\"version\":1,\"presentations\":[{\"slides\":[{\"mediaPath\":\"" + badMedia + "\"}]}]}");
                using var output = archive.CreateEntry(badMedia).Open();
                output.WriteByte(1);
            }
            try { ShowPackage.Open(package, Path.Combine(folder, "cache")); throw new Exception("FAIL bad media hash accepted"); }
            catch (InvalidDataException) { }
            Check(!Directory.EnumerateFiles(Path.Combine(folder, "cache"), "*", SearchOption.AllDirectories).Any(),
                "corrupt media is not cached");
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
            File.WriteAllText(path, """{"name":"Song","presentationKind":"song","slides":[{"name":"Verse","backgroundFill":{"colorHex":"#123456FF"},"objects":[{"objectKind":"text","text":"Hello","textStyle":{"fontName":"HelveticaNeue-Bold","fontSize":108,"colorHex":"#FFEEDDFF","horizontalAlignment":"left"}},{"objectKind":"text","text":"Hidden","hidden":true},{"objectKind":"shape","text":"Ignored"}]}]}""");
            var imported = MxuPresentationImport.Load(path);
            Check(imported.Slides[0].Text == "Hello", "visible lyrics");
            Check(imported.Slides[0].Background == "#123456FF", "background color");
            Check(imported.Slides[0].TextStyle?.FontName == "Segoe UI" &&
                imported.Slides[0].TextStyle.FontSize == 54 &&
                imported.Slides[0].TextStyle.ColorHex == "#FFEEDDFF" &&
                imported.Slides[0].TextStyle.HorizontalAlignment == "left" && imported.Slides[0].TextStyle.Weight == "bold",
                "MxU text style subset maps to the Windows design canvas");
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
