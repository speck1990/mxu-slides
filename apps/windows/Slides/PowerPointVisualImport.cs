using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Slides.Core;

namespace Slides;

// Uses an installed PowerPoint to render slide visuals. The resulting deck is
// self-contained image media and does not need PowerPoint during presentation.
internal static class PowerPointVisualImport
{
    public static Task<Presentation> LoadAsync(string path, string cacheRoot)
    {
        var completion = new TaskCompletionSource<Presentation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Load(path, cacheRoot)); }
            catch (Exception error) { completion.SetException(error); }
        })
        { IsBackground = true, Name = "Slides PowerPoint import" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static Presentation Load(string path, string cacheRoot)
    {
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Choose an existing .pptx file.", path);
        var powerPointType = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new InvalidOperationException(
                "Microsoft PowerPoint is required for visual import. Use PPTX Editable for a basic import without PowerPoint.");
        var runningProcesses = Process.GetProcessesByName("POWERPNT");
        var wasRunning = runningProcesses.Length > 0;
        foreach (var process in runningProcesses) process.Dispose();
        var staging = Path.Combine(cacheRoot, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        object? application = null;
        object? presentations = null;
        object? document = null;
        object? slides = null;
        object? pageSetup = null;
        try
        {
            application = Activator.CreateInstance(powerPointType)
                ?? throw new InvalidOperationException("PowerPoint could not be started.");
            dynamic app = application;
            presentations = app.Presentations;
            dynamic openPresentations = presentations;
            document = openPresentations.Open(Path.GetFullPath(path), -1, 0, 0);
            dynamic deck = document;
            pageSetup = deck.PageSetup;
            dynamic setup = pageSetup;
            double slideWidth = setup.SlideWidth;
            double slideHeight = setup.SlideHeight;
            if (slideWidth <= 0 || slideHeight <= 0)
                throw new InvalidDataException("PowerPoint reported an invalid slide size.");
            var exportWidth = 1920;
            var exportHeight = Math.Clamp((int)Math.Round(exportWidth * slideHeight / slideWidth), 240, 3840);
            slides = deck.Slides;
            dynamic slideCollection = slides;
            int count = slideCollection.Count;
            if (count < 1 || count > 1000)
                throw new InvalidDataException("The PowerPoint deck must contain between 1 and 1000 slides.");
            var output = new Presentation { Name = Path.GetFileNameWithoutExtension(path), Kind = "deck" };
            for (var index = 1; index <= count; index++)
            {
                object? slide = null;
                try
                {
                    slide = slideCollection[index];
                    var exported = Path.Combine(staging, $"slide-{index:0000}.png");
                    dynamic page = slide;
                    page.Export(exported, "PNG", exportWidth, exportHeight);
                    if (!File.Exists(exported) || new FileInfo(exported).Length == 0)
                        throw new IOException($"PowerPoint did not export slide {index}.");
                    var hash = Hash(exported);
                    var destination = Path.Combine(cacheRoot, hash + ".png");
                    if (!File.Exists(destination)) File.Move(exported, destination);
                    output.Slides.Add(new Slide
                    {
                        Name = $"Slide {index}", Background = "#000000FF",
                        Elements = [new SlideElement
                        {
                            Kind = "image", Name = "PowerPoint slide",
                            X = 0, Y = 0, Width = 960, Height = 540,
                            MediaPath = destination, MediaScaleMode = "fit"
                        }]
                    });
                }
                finally { Release(slide); }
            }
            return output;
        }
        finally
        {
            Release(pageSetup);
            Release(slides);
            try { if (document is not null) { dynamic deck = document; deck.Close(); } }
            catch (COMException) { /* PowerPoint may have closed during an import error. */ }
            Release(document);
            Release(presentations);
            try { if (!wasRunning && application is not null) { dynamic app = application; app.Quit(); } }
            catch (COMException) { /* The exported images remain usable. */ }
            Release(application);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
