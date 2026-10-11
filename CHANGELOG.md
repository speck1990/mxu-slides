# Changelog

## Unreleased

### Added

- Windows positioned image objects with fit, fill, or stretch scaling on the slide canvas; portable shows and media relinking now include these images.
- Windows Edit Layout canvas dragging and corner resizing for positioned objects, plus solid ellipses and ellipse import from standalone MxU Presentation JSON.
- Windows positioned slide layout editing for multiple text objects and solid rectangles; basic Mac Presentation JSON object geometry now imports onto the shared 16:9 canvas.
- Windows per-slide typography controls and four Mac-inspired starter palettes, basic MxU text-style import, and consistent 16:9 scaling between preview and audience projection.
- Windows slide deck editor for creating presentations and editing slide text, solid backgrounds, media, and slide order, with Cancel leaving the show untouched.
- Portable Windows `.slidespack` show export/import with embedded images and videos, verified media hashes, and a persistent local media cache.
- Windows library folders with create, rename, delete, and move actions; manual relinking for moved images and videos with ambiguity protection.
- Persistent Windows song and slide library shared across shows, one-time import from existing `.pshow` files, and a New Show action that keeps the library.
- Microsoft Defender scanning and a SHA-256 record for Windows CI downloads.
- Recognize unbracketed song section headings on Windows, repair headings saved by earlier Windows builds, and preserve section labels when editing songs.
- Windows workspace with separate show-scoped library and ordered service flow, slide thumbnails, Mac-style panel layout, and backward-compatible `.pshow` loading.
- Personal Windows preview under the distinct **Slides** name, including `Slides.exe` and a matching build artifact.
- Repeatable macOS debug build, local installation, and launch script with a Codex Run action.
- Concurrent Windows and Apple silicon macOS GitHub Actions builds with downloadable artifacts and the required license notice.
- Native WPF control window with offline song parsing, show autosave, standalone MxU Presentation JSON text import, local image/video slides, output preview, keyboard navigation, black screen, and fullscreen audience output on a selected display.
- Windows GitHub Actions test, self-contained build artifact, manual build, and optional prerelease workflow.

### Fixed

- Copying presentations through the Windows library now retains per-slide typography and positioned objects.

### Known limits

- MxU library database, advanced slide objects, themes, NDI, stage display, and DeckLink are not yet implemented on Windows.
- Windows build and media behavior require validation on GitHub Actions and a Windows PC.
