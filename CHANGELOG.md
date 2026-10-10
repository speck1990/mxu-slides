# Changelog

## Unreleased

### Added

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

### Known limits

- MxU library database, advanced slide objects, themes, media relinking, NDI, stage display, and DeckLink are not yet implemented on Windows.
- Windows build and media behavior require validation on GitHub Actions and a Windows PC.
