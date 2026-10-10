# Slides for Windows

Personal Windows port of MxU Slides. It keeps the original source history and license but uses a distinct application name. See the repository's `LICENSE.md` for the required MxU notice and distribution terms.

The [Windows parity tracker](../../docs/windows-parity.md) records what still differs from the Mac app. This preview does not yet provide full Mac functionality or identical rendering.

## Preview features

- The workspace follows the Mac app's main layout: **Service Flow** and **Libraries** on the left, slide thumbnails in the center, and **Output Preview** and projection controls on the right. The Windows library is currently stored inside each `.pshow` file.
- Create songs with **New Song** or **+ New**. Blank lines divide slides; `[Verse]` and `[Chorus]` mark sections. Add a library item to the service again by selecting it and pressing **+ Add Item** or double-clicking it. Reorder service items with the arrow buttons; removing one keeps its library copy.
- Add a local image or video as a slide. Playback uses Windows media codecs and works without a network connection.
- Import a standalone MxU Presentation JSON file. The preview reads visible text objects and solid slide backgrounds. It does not import the macOS library database or advanced slide designs.
- Choose an audience display and start fullscreen projection with F5. Use Left/Right to navigate and B to black out. Shows autosave under `%LOCALAPPDATA%\Slides` and can be opened or saved as `.pshow` files.
- Existing `.pshow` files open with their original presentation order as the service. Saving them adds the service order while preserving the library entries.

## Download and run

Open the [build workflow](../../actions/workflows/builds.yml), choose a successful run, and download the `Slides-win-x64` artifact. Extract it and run `Slides.exe`; keep all extracted files in the same folder. No .NET installation or manual compilation is required. The executable is unsigned, so Windows may ask you to confirm it on first launch. For a new build, select **Run workflow**. A prerelease zip can be produced by enabling the `prerelease` option for a manual run after checking distribution rights.

## Developer commands

Requires the .NET 8 SDK on Windows:

```powershell
dotnet run --project apps/windows/Slides.Tests/Slides.Tests.csproj -c Release
dotnet publish apps/windows/Slides/Slides.csproj -c Release -r win-x64 --self-contained true
```

The workflow is the source of truth for Windows compilation. The core tests also run on Linux with .NET 8.
