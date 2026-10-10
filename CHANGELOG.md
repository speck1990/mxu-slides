# Changelog

## Unreleased

### Added

- SongSelect browser-assisted text lyric import on Mac and Windows. Users sign in and search on SongSelect, then load the downloaded text file; Slides retains sections and CCLI credits. Live download use still needs validation with a licensed account.
- Personal Windows preview under the distinct **Slides** name, including `Slides.exe` and a matching build artifact.
- Repeatable macOS debug build, local installation, and launch script with a Codex Run action.
- Concurrent Windows and Apple silicon macOS GitHub Actions builds with downloadable artifacts and the required license notice.
- Native WPF control window with offline song parsing, show autosave, standalone MxU Presentation JSON text import, local image/video slides, output preview, keyboard navigation, black screen, and fullscreen audience output on a selected display.
- Windows GitHub Actions test, self-contained build artifact, manual build, and optional prerelease workflow.

### Known limits

- MxU library database, advanced slide objects, themes, media relinking, NDI, stage display, and DeckLink are not yet implemented on Windows.
- Windows build and media behavior require validation on GitHub Actions and a Windows PC.
