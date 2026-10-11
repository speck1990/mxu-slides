# Slides for Windows

Personal Windows port of MxU Slides. It keeps the original source history and license but uses a distinct application name. See the repository's `LICENSE.md` for the required MxU notice and distribution terms.

The [Windows parity tracker](../../docs/windows-parity.md) records what still differs from the Mac app. This preview does not yet provide full Mac functionality or identical rendering.

## Preview features

- The workspace follows the Mac app's main layout: **Service Flow** and **Libraries** on the left, slide thumbnails in the center, and **Output Preview** and projection controls on the right. The library persists locally across shows in `%LOCALAPPDATA%\Slides\library.json`. Use the library folder selector to view All Slides, Top Level, or one folder; create, rename, delete, and move items between folders. Deleting a folder moves its items to Top Level.
- Create songs with **New Song** or **+ New**. Blank lines divide slides; `[Verse]` and `[Chorus]` mark sections. Add a library item to the service again by selecting it and pressing **+ Add Item** or double-clicking it. Reorder service items with the arrow buttons; removing one keeps its library copy.
- Create a slide presentation with **New Deck**. Select a service item and use **Edit Slides** to change slide names, text, background colors, and local media; add, duplicate, remove, or reorder slides. The editor also works on songs and imported presentations. **Edit Song** remains the quick way to edit lyrics and section headings. Saving slide edits updates the current show and its library copy; Cancel discards them.
- **Edit Slides** also offers font family, size, color, weight, and alignment for each slide. The **Starter look** selector applies Clean Geometric, Editorial Serif, Bold Blocks, or Digital colors and Windows font substitutes to all slides in the current presentation. New slides inherit the preceding slide's look. Existing shows retain their original appearance until edited. The preview and fullscreen audience window use the same 16:9 design canvas, with letterboxing on displays of another shape. Imported standalone MxU Presentation JSON can use basic font, size, color, and alignment; complex object effects remain unsupported.
- For multiple text blocks, solid shapes, or local images, select a slide in **Edit Slides** and open **Edit Layout**. Choose **+ Image** to place a PNG, JPEG, BMP, or GIF, then select fit, fill, or stretch. Drag an object to move it, or drag the green corner of the selected object to resize it. Set rotation, opacity, and horizontal or vertical flipping for any object. Numeric position and size fields remain available on the 960 × 540 canvas, along with controls to move objects back or forward in the draw order. The preview updates before Save; Cancel leaves the presentation untouched. A standalone MxU Presentation JSON file keeps supported positioned text and solid shapes at half its 1920 × 1080 coordinates and imports their rotation, opacity, and flips; Mac media objects still need their original media library. Slides with missing text coordinates remain readable as one flat text slide.
- Add a local image or video as a slide. Playback uses Windows media codecs and works without a network connection.
- Import a standalone MxU Presentation JSON file. The preview reads visible text objects and solid slide backgrounds. It does not import the macOS library database or advanced slide designs.
- Choose an audience display and start fullscreen projection with F5. Use Left/Right to navigate and B to black out. Shows autosave under `%LOCALAPPDATA%\Slides` and can be opened or saved as `.pshow` files. **New Show** clears the service while keeping the library; use **Save As** first to keep a copy of the current service.
- Existing `.pshow` files open with their original presentation order as the service. Their presentations are imported into the local library once. A `.pshow` keeps its own presentation snapshots, so older shows remain self-contained; editing a song in the current service also updates its library copy. Folder placement stays local to the library and does not change a `.pshow`.
- Ordinary `.pshow` files keep local media paths. To move a service with its images, videos, and positioned image objects, choose **Pack Show** to create a `.slidespack` file, then **Open Show** on the other Windows PC. Slides verifies and copies packaged media into `%LOCALAPPDATA%\Slides\packages`; keep that cache if you want those shows to continue playing without the package file. Packing stops with a clear error if any media is missing. The package can be large because it includes the media files.
- For an ordinary `.pshow` moved without its media, choose **Relink Media** and select a folder containing the images and videos. Slides searches that folder and its subfolders by filename. A missing file stays unresolved, and multiple files with the same name are left unchanged for you to resolve manually.

## Download and run

Open the [build workflow](../../actions/workflows/builds.yml), choose a successful run, and download the `Slides-win-x64` artifact. Extract it and run `Slides.exe`; keep all extracted files in the same folder. No .NET installation or manual compilation is required. The executable is unsigned, so Windows may ask you to confirm it on first launch. For a new build, select **Run workflow**. A prerelease zip can be produced by enabling the `prerelease` option for a manual run after checking distribution rights.

## Developer commands

Requires the .NET 8 SDK on Windows:

```powershell
dotnet run --project apps/windows/Slides.Tests/Slides.Tests.csproj -c Release
dotnet publish apps/windows/Slides/Slides.csproj -c Release -r win-x64 --self-contained true
```

The workflow is the source of truth for Windows compilation. The core tests also run on Linux with .NET 8.
