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
            Check(show.ServiceDetails!.Count == 2 && show.ServiceDetails.All(item => item.Section == "Service") &&
                show.ServiceDate is null, "legacy service defaults stay readable");
            show.RemoveFromService(0);
            Check(show.Presentations.Count == 2 && show.ServiceItems!.SequenceEqual(["b"]), "removal keeps library");
            show.AddToService("a");
            show.AddToService("a");
            show.ServiceDate = new DateOnly(2026, 10, 18);
            show.UpdateServiceItem(2, "Pre-Service", "Play entrance video", 4);
            var plannedId = show.ServiceDetails![2].Id;
            show.MoveServiceItem(2, 0);
            Check(show.ServiceDetails[0].Id == plannedId && show.ServiceDetails[0].Notes == "Play entrance video" &&
                show.ServiceDetails[0].PlannedMinutes == 4, "moving a repeated item keeps its own plan");
            ShowFiles.Save(path, show);
            var reopened = ShowFiles.Load(path);
            Check(reopened.ServiceItems!.SequenceEqual(["a", "b", "a"]) &&
                reopened.ServiceDate == new DateOnly(2026, 10, 18), "service order and date survive save");
            Check(reopened.ServiceDetails![0].Id == plannedId && reopened.ServiceDetails[0].Section == "Pre-Service" &&
                reopened.ServiceDetails[2].Notes == "", "repeated items retain separate planning details");
        }
        finally { File.Delete(path); }
    }),
    ("service plan rejects invalid details without changing an older show", () =>
    {
        var show = new Show { Presentations = [SongParser.Parse("Song", "Lyrics")], ServiceItems = [] };
        show.EnsureServiceItems();
        show.AddToService(show.Presentations[0].Id);
        try { show.UpdateServiceItem(0, "Other", "", null); throw new Exception("FAIL unknown section accepted"); }
        catch (ArgumentException) { }
        try { show.UpdateServiceItem(0, "Service", "", 481); throw new Exception("FAIL long duration accepted"); }
        catch (ArgumentException) { }
        Check(show.ServiceDetails![0].Section == "Service" && show.ServiceDetails[0].PlannedMinutes is null,
            "invalid edits leave the plan intact");
        show.ServiceDetails!.Clear();
        try { show.EnsureServiceItems(); throw new Exception("FAIL mismatched service details accepted"); }
        catch (InvalidDataException) { }
    }),
    ("overlays stay in the library and portable show", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var libraryPath = Path.Combine(folder, "library.json");
            var showPath = Path.Combine(folder, "service.pshow");
            var packagePath = Path.Combine(folder, "service.slidespack");
            var overlay = new Overlay { Name = "Verse + Reference", Text = "John 3:16\nFor God so loved the world" };
            var library = new PresentationLibrary();
            library.UpsertOverlay(overlay);
            var show = new Show { ServiceItems = [] };
            library.AddOverlayToShow(show, overlay.Id);
            LibraryFiles.Save(libraryPath, library);
            ShowFiles.Save(showPath, show);
            ShowPackage.Save(packagePath, show);
            Check(LibraryFiles.Load(libraryPath).Overlays.Single().Text == overlay.Text,
                "overlay remains available after library reload");
            Check(ShowFiles.Load(showPath).Overlays.Single().Name == overlay.Name &&
                ShowPackage.Open(packagePath, Path.Combine(folder, "cache")).Overlays.Single().Id == overlay.Id,
                "overlay survives show and package round trips");
            var nextShow = new Show { ServiceItems = [] };
            LibraryFiles.Load(libraryPath).AddOverlayToShow(nextShow, overlay.Id);
            Check(nextShow.Overlays.Single().Id == overlay.Id, "overlay can be reused in a new show");
            overlay.Layout = "unsupported";
            try { library.UpsertOverlay(overlay); throw new Exception("FAIL invalid overlay accepted"); }
            catch (InvalidDataException) { }
        }
        finally { Directory.Delete(folder, true); }
    }),
    ("live service timing follows occurrences and survives pause and reorder", () =>
    {
        var clock = new ManualClock();
        var run = new ServiceRun(clock);
        var show = new Show { Presentations = [SongParser.Parse("Song", "Lyrics")], ServiceItems = [] };
        show.AddToService(show.Presentations[0].Id);
        show.AddToService(show.Presentations[0].Id);
        var first = show.ServiceDetails![0].Id;
        var second = show.ServiceDetails[1].Id;
        run.Select(first);
        run.Start();
        clock.Advance(TimeSpan.FromSeconds(25));
        run.Select(second);
        clock.Advance(TimeSpan.FromSeconds(15));
        run.Pause();
        clock.Advance(TimeSpan.FromSeconds(20));
        Check(run.ServiceElapsed == TimeSpan.FromSeconds(40) &&
            run.ItemElapsed(first) == TimeSpan.FromSeconds(25) &&
            run.ItemElapsed(second) == TimeSpan.FromSeconds(15), "pause and switch track each occurrence");
        show.MoveServiceItem(1, 0);
        run.Select(show.ServiceDetails![0].Id);
        run.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(run.CurrentItemElapsed == TimeSpan.FromSeconds(25) &&
            run.ServiceElapsed == TimeSpan.FromSeconds(50), "reorder retains occurrence timing");
        run.Reset();
        Check(!run.IsRunning && run.ServiceElapsed == TimeSpan.Zero &&
            run.ItemElapsed(first) == TimeSpan.Zero && run.ItemElapsed(second) == TimeSpan.Zero,
            "reset clears the live run");
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
    ("positioned layout edits preserve order and save with the show", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            var slide = new Slide { Text = "Welcome" };
            var draft = SlideLayout.BeginEdit(slide);
            Check(draft.Count == 1 && draft[0].Text == "Welcome" && slide.Elements is null,
                "flat slide opens as an editable text object without mutation");
            draft[0].X = 140;
            draft[0].Width = 420;
            var shape = SlideLayout.AddShape(draft);
            shape.FillColorHex = "#3B82F6FF";
            SlideLayout.Move(draft, 1, 0);
            var duplicate = SlideLayout.Duplicate(draft, 1);
            duplicate.Text = "Next steps";
            SlideLayout.Apply(slide, draft);
            var savedElements = slide.Elements!;
            Check(savedElements.Select(item => item.Kind).SequenceEqual(["shape", "text", "text"]),
                "object stacking order persists");
            Check(savedElements[1].X == 140 && savedElements[1].Width == 420 &&
                savedElements[2].Id != savedElements[1].Id, "geometry and independent IDs");
            Check(slide.Text == "Welcome\nNext steps", "flat text summary follows positioned text");
            var deck = new Presentation { Kind = "deck", Slides = [slide] };
            PresentationEditing.Validate(deck);
            ShowFiles.Save(path, new Show { Presentations = [deck] });
            var reopened = ShowFiles.Load(path).Presentations[0].Slides[0];
            Check(reopened.Elements?.Count == 3 && reopened.Elements[0].FillColorHex == "#3B82F6FF",
                "layout survives show save");
        }
        finally { File.Delete(path); }
    }),
    ("positioned layout rejects impossible bounds", () =>
    {
        var objects = SlideLayout.BeginEdit(new Slide { Text = "Text" });
        objects[0].Width = double.NaN;
        try { SlideLayout.Validate(objects); throw new Exception("FAIL NaN geometry accepted"); }
        catch (ArgumentException) { }
        objects[0].Width = 800;
        objects[0].X = 2000;
        try { SlideLayout.Validate(objects); throw new Exception("FAIL off-canvas geometry accepted"); }
        catch (ArgumentException) { }
    }),
    ("canvas gestures and ellipse layouts survive library and show copies", () =>
    {
        var text = new SlideElement { Name = "Title", Text = "Welcome", X = 40, Y = 50, Width = 300, Height = 100 };
        SlideLayout.Translate(text, 25, -10);
        SlideLayout.Resize(text, 80, 30);
        Check(text.X == 65 && text.Y == 40 && text.Width == 380 && text.Height == 130,
            "drag and resize use the slide design canvas");
        SlideLayout.Resize(text, -1000, -1000);
        Check(text.Width == 12 && text.Height == 12, "resize keeps an object usable");
        var elements = new List<SlideElement> { text };
        var ellipse = SlideLayout.AddEllipse(elements);
        ellipse.FillColorHex = "#FFAA00FF";
        ellipse.RotationDegrees = 35;
        ellipse.Opacity = 0.65;
        ellipse.FlipHorizontal = true;
        ellipse.FlipVertical = true;
        var slide = new Slide { TextStyle = new SlideTextStyle { FontSize = 61 } };
        SlideLayout.Apply(slide, elements);
        var deck = new Presentation { Kind = "deck", Slides = [slide] };
        var library = new PresentationLibrary();
        library.Upsert(deck);
        Check(library.Presentations[0].Slides[0].TextStyle?.FontSize == 61 &&
            library.Presentations[0].Slides[0].Elements?[1].ShapeKind == "ellipse" &&
            library.Presentations[0].Slides[0].Elements?[1].RotationDegrees == 35 &&
            library.Presentations[0].Slides[0].Elements?[1].Opacity == 0.65 &&
            library.Presentations[0].Slides[0].Elements?[1].FlipHorizontal == true,
            "library copy retains typography and objects");
        var show = new Show { ServiceItems = [] };
        library.AddToShow(show, deck.Id);
        show.Presentations[0].Slides[0].Elements![1].FillColorHex = "#000000FF";
        Check(library.Presentations[0].Slides[0].Elements![1].FillColorHex == "#FFAA00FF",
            "show snapshot does not modify the library layout");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pshow");
        try
        {
            ShowFiles.Save(path, show);
            var saved = ShowFiles.Load(path).Presentations[0].Slides[0].Elements![1];
            Check(saved.ShapeKind == "ellipse" && saved.RotationDegrees == 35 &&
                saved.Opacity == 0.65 && saved.FlipHorizontal && saved.FlipVertical,
                "ellipse transforms survive a show save");
        }
        finally { File.Delete(path); }
    }),
    ("object effects validate ranges and keep legacy defaults", () =>
    {
        var old = JsonSerializer.Deserialize<SlideElement>("""{"kind":"shape","name":"Old rectangle"}""", ShowFiles.Json)!;
        Check(old.RotationDegrees == 0 && old.Opacity == 1 && !old.FlipHorizontal && !old.FlipVertical,
            "older layouts retain their appearance");
        old.Opacity = double.NaN;
        try { SlideLayout.Validate([old]); throw new Exception("FAIL NaN opacity accepted"); }
        catch (ArgumentException) { }
        old.Opacity = 1.1;
        try { SlideLayout.Validate([old]); throw new Exception("FAIL opacity above 100 accepted"); }
        catch (ArgumentException) { }
        old.Opacity = 1;
        old.RotationDegrees = double.PositiveInfinity;
        try { SlideLayout.Validate([old]); throw new Exception("FAIL infinite rotation accepted"); }
        catch (ArgumentException) { }
    }),
    ("positioned images keep their fit mode and reject missing files", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "photo.png");
            File.WriteAllBytes(path, [1, 2, 3]);
            var items = new List<SlideElement>();
            var image = SlideLayout.AddImage(items, path);
            image.MediaScaleMode = "fill";
            SlideLayout.Resize(image, -120, 40);
            SlideLayout.AddText(items).Text = "Original lyric";
            var slide = new Slide();
            SlideLayout.Apply(slide, items);
            Check(slide.Elements?[0].Kind == "image" && slide.Elements[0].MediaPath == path &&
                slide.Elements[0].MediaScaleMode == "fill" && slide.Elements[0].Width == 360,
                "positioned image and fit mode copy into a slide");
            var library = new PresentationLibrary();
            var deck = new Presentation { Slides = [slide] };
            library.Upsert(deck);
            var show = new Show { ServiceItems = [] };
            library.AddToShow(show, deck.Id);
            Check(show.Presentations[0].Slides[0].Elements?[0].MediaPath == path,
                "image object survives library to show copy");
            var editedSong = SongParser.Parse("Song", "Verse 1\nUpdated lyric");
            PresentationEditing.PreserveSongDesign(deck, editedSong);
            Check(editedSong.Slides[0].Elements?[0].MediaPath == path &&
                editedSong.Slides[0].Elements?[1].Text == "Updated lyric",
                "quick lyric edit keeps the image and refreshes positioned text");
            File.Delete(path);
            try { SlideLayout.Validate(slide.Elements!); throw new Exception("FAIL missing image accepted"); }
            catch (ArgumentException) { }
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
            var inset = Path.Combine(media, "inset.png");
            File.WriteAllText(unique, "image");
            File.WriteAllText(inset, "inset");
            File.WriteAllText(Path.Combine(media, "duplicate.png"), "first");
            File.WriteAllText(Path.Combine(other, "duplicate.png"), "second");
            var oldRoot = Path.Combine(folder, "old");
            var presentation = new Presentation { Slides =
            [
                new Slide { MediaPath = Path.Combine(oldRoot, "screen.png"), Elements =
                    [new SlideElement { Kind = "image", MediaPath = Path.Combine(oldRoot, "inset.png") }] },
                new Slide { MediaPath = Path.Combine(oldRoot, "duplicate.png") },
                new Slide { MediaPath = Path.Combine(oldRoot, "missing.png") },
                new Slide { MediaPath = unique }
            ] };
            var result = MediaRelinker.Relink([presentation], folder);
            Check(result == new MediaRelinkResult(2, 1, 1), "relink counts include positioned images");
            Check(presentation.Slides[0].MediaPath == unique, "unique file relinked");
            Check(presentation.Slides[0].Elements![0].MediaPath == inset, "positioned image relinked");
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
            var inset = Path.Combine(folder, "inset.png");
            var package = Path.Combine(folder, "service.slidespack");
            File.WriteAllBytes(image, [1, 2, 3, 4]);
            File.WriteAllBytes(video, [5, 6, 7]);
            File.WriteAllBytes(inset, [8, 9, 10]);
            var presentation = new Presentation { Name = "Media", Slides =
            [
                new Slide { Name = "Image", MediaPath = image, Elements =
                    [new SlideElement { Name = "Title", Text = "Welcome", X = 120, Y = 80, Width = 500, Height = 160 },
                     new SlideElement { Kind = "image", Name = "Inset", MediaPath = inset, MediaScaleMode = "fill",
                         X = 500, Y = 260, Width = 300, Height = 200 }] },
                new Slide { Name = "Video", MediaPath = video },
                new Slide { Name = "Image again", MediaPath = image }
            ] };
            var show = new Show { Presentations = [presentation], ServiceItems = [presentation.Id, presentation.Id] };
            show.EnsureServiceItems();
            show.ServiceDate = new DateOnly(2026, 10, 18);
            show.UpdateServiceItem(1, "Post-Service", "Fade to black", 2);
            ShowPackage.Save(package, show);
            Check(show.Presentations[0].Slides[0].MediaPath == image, "packing does not change the open show");
            using (var archive = ZipFile.OpenRead(package))
            {
                Check(archive.Entries.Count(entry => entry.FullName.StartsWith("media/")) == 3,
                    "reused media is packed once");
                using var reader = new StreamReader(archive.GetEntry("show.json")!.Open());
                var packedJson = reader.ReadToEnd();
                Check(!packedJson.Contains(image, StringComparison.Ordinal) &&
                    !packedJson.Contains(inset, StringComparison.Ordinal), "package omits original absolute paths");
            }
            File.Delete(image);
            File.Delete(video);
            File.Delete(inset);
            var opened = ShowPackage.Open(package, Path.Combine(folder, "cache"));
            Check(opened.ServiceItems!.SequenceEqual([presentation.Id, presentation.Id]), "service order survives packaging");
            Check(opened.ServiceDate == new DateOnly(2026, 10, 18) &&
                opened.ServiceDetails![1].Section == "Post-Service" && opened.ServiceDetails[1].Notes == "Fade to black",
                "service plan survives portable packaging");
            Check(File.ReadAllBytes(opened.Presentations[0].Slides[0].MediaPath!).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "image works without its original path");
            Check(File.ReadAllBytes(opened.Presentations[0].Slides[1].MediaPath!).SequenceEqual(new byte[] { 5, 6, 7 }),
                "video works without its original path");
            Check(opened.Presentations[0].Slides[0].MediaPath == opened.Presentations[0].Slides[2].MediaPath,
                "repeated media uses one cached copy");
            Check(opened.Presentations[0].Slides[0].Elements?[0].Text == "Welcome" &&
                opened.Presentations[0].Slides[0].Elements?[0].X == 120,
                "positioned layout survives portable packaging");
            var openedInset = opened.Presentations[0].Slides[0].Elements![1];
            Check(File.ReadAllBytes(openedInset.MediaPath!).SequenceEqual(new byte[] { 8, 9, 10 }) &&
                openedInset.MediaScaleMode == "fill", "positioned image survives without its original file");
            var library = new PresentationLibrary();
            library.ImportMissing(show.Presentations);
            library.Presentations[0].Name = "My edited library title";
            Check(library.RepairMissingMediaFrom(opened.Presentations), "package repairs stale library media paths");
            Check(library.Presentations[0].Slides[0].MediaPath == opened.Presentations[0].Slides[0].MediaPath &&
                library.Presentations[0].Name == "My edited library title", "repair preserves library edits");
            Check(library.Presentations[0].Slides[0].Elements![1].MediaPath == openedInset.MediaPath,
                "package repairs positioned image in the library");
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
            show.Presentations[0].Slides[0].MediaPath = null;
            show.Presentations[0].Slides[0].Elements = [new SlideElement { Kind = "image", Name = "Missing image" }];
            try { ShowPackage.Save(package, show); throw new Exception("FAIL empty image reference packed"); }
            catch (FileNotFoundException) { }
            Check(!File.Exists(package), "missing positioned image does not create a package");

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
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("format.txt").Open()))
                    writer.Write("Slides portable show 1\n");
                using (var writer = new StreamWriter(archive.CreateEntry("show.json").Open()))
                    writer.Write("""{"version":1,"presentations":[{"slides":[{"elements":[{"kind":"image","mediaPath":"../../escape.png"}]}]}]}""");
            }
            try { ShowPackage.Open(package, Path.Combine(folder, "cache")); throw new Exception("FAIL unsafe positioned image accepted"); }
            catch (InvalidDataException) { }
            Check(!Directory.Exists(Path.Combine(folder, "cache")), "unsafe positioned image extracts nothing");

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
            var importedStyle = imported.Slides[0].TextStyle;
            Check(importedStyle is not null && importedStyle.FontName == "Segoe UI" &&
                importedStyle.FontSize == 54 && importedStyle.ColorHex == "#FFEEDDFF" &&
                importedStyle.HorizontalAlignment == "left" && importedStyle.Weight == "bold",
                "MxU text style subset maps to the Windows design canvas");
        }
        finally { File.Delete(path); }
    }),
    ("MxU positioned text and shapes use the Mac canvas geometry", () =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"name":"Welcome","presentationKind":"deck","slides":[{"name":"Intro","objects":[{"objectKind":"shape","shapeKind":"roundedRectangle","name":"Blue plate","text":"","x":200,"y":100,"width":800,"height":300,"cornerRadius":20,"rotationDegrees":15,"opacity":0.75,"flipHorizontal":true,"fill":{"fillKind":"solid","colorHex":"#3B82F6FF"}},{"objectKind":"text","name":"Title","text":"Hello","x":240,"y":140,"width":720,"height":220,"flipVertical":true,"textStyle":{"fontName":"Georgia","fontSize":96,"colorHex":"#FFFFFFFF"}}]}]}""");
            var slide = MxuPresentationImport.Load(path).Slides[0];
            var importedElements = slide.Elements!;
            Check(importedElements.Count == 2 && importedElements[0].Kind == "shape" &&
                importedElements[0].X == 100 && importedElements[0].Width == 400 &&
                importedElements[0].CornerRadius == 10, "Mac rectangle scaled to Windows canvas");
            Check(importedElements[0].RotationDegrees == 15 && importedElements[0].Opacity == 0.75 &&
                importedElements[0].FlipHorizontal && importedElements[1].FlipVertical,
                "Mac rotation, opacity, and flips map to Windows objects");
            Check(importedElements[1].Text == "Hello" && importedElements[1].X == 120 &&
                importedElements[1].TextStyle?.FontSize == 48, "positioned text and style imported");
            Check(slide.Text == "Hello", "text summary remains available");
            File.WriteAllText(path, """{"slides":[{"objects":[{"objectKind":"shape","shapeKind":"ellipse","x":100,"y":200,"width":400,"height":200,"fill":{"fillKind":"solid","colorHex":"#FFFFFFFF"}}]}]}""");
            var ellipse = MxuPresentationImport.Load(path).Slides[0].Elements![0];
            Check(ellipse.ShapeKind == "ellipse" && ellipse.X == 50 && ellipse.Height == 100,
                "Mac ellipse imports with scaled geometry");
            File.WriteAllText(path, """{"slides":[{"objects":[{"objectKind":"text","text":"No coordinates"},{"objectKind":"shape","shapeKind":"rectangle","x":0,"y":0,"width":100,"height":100,"fill":{"fillKind":"solid","colorHex":"#FFFFFFFF"}}]}]}""");
            var fallback = MxuPresentationImport.Load(path).Slides[0];
            Check(fallback.Elements is null && fallback.Text == "No coordinates",
                "missing text geometry keeps the readable flat slide");
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

sealed class ManualClock : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
}
