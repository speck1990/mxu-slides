# Codex guide for this fork

## Purpose and ownership

This is Stephen Peck's personal Windows port of MxU Slides. Keep MxU's original commit history, `LICENSE.md`, required notice, copyright statements, and third-party notices. The upstream project is `MxU-Engineering/mxu-slides`; set it as the `upstream` Git remote. The Windows application uses the distinct product name **Slides** because the original license reserves the MxU name and logo. Read `LICENSE.md` before changing distribution.

## Architecture

- `apps/mac`: existing SwiftUI macOS application. Its Swift packages under `apps/mac/Packages` include `PresenterCore`, scene/rendering, media, outputs, and device integrations. Preserve this tree during Windows work.
- `packages/schema/documents.json`: canonical portable document schema, shared by the macOS code generator and Windows compatibility tests. Change it through `node packages/schema/codegen.mjs`, retaining backward-compatible optional fields.
- `apps/windows/Slides.Core`: offline show files, lyric parsing, and read-only import of the MxU Presentation JSON subset. Keep it free of WPF and platform APIs.
- `apps/windows/Slides`: Windows WPF control window, local image/video playback, and audience window.
- `apps/windows/Slides.Tests`: dependency-free regression runner. The `.github/workflows/windows.yml` Windows runner is the authoritative Windows compile check.

The Windows importer currently supports text objects and solid background colors from a standalone MxU Presentation JSON file. It does not read the macOS Automerge library or render advanced objects. Preserve unknown fields at the schema level; do not claim full library compatibility until it is implemented.

## Conventions and tests

- Use .NET 8 and built-in WPF/.NET libraries for the first milestones. Keep the Windows app offline by default; do not require an account or external service.
- Use nullable C# and clear exceptions for bad files. Keep document parsing and song logic in Core. Avoid absolute paths in committed data or tests.
- Add focused regression coverage for changes to song parsing, persistence, or MxU document import. Run `dotnet run --project apps/windows/Slides.Tests/Slides.Tests.csproj --configuration Release` and `dotnet publish apps/windows/Slides/Slides.csproj -c Release -r win-x64 --self-contained true` on Windows.
- Verify the GitHub Actions run completes successfully before stating that a downloadable Windows build works. A local Linux workspace cannot compile WPF.
- For macOS changes, follow the build and package tests in the root `README.md`. Keep generated Xcode projects out of Git.
- On a local Mac, use `./script/build_and_run.sh --verify` to build, install to `~/Applications`, launch, and verify the original macOS app. The `.codex/environments/environment.toml` Run action uses this script. Do not claim a Mac installation from a Linux or remote cloud workspace.

## Workflow

- Keep `main` aligned with reviewed milestones. Branch from `main` using `windows/<milestone>` for Windows work and short topic branches for other changes. Open manageable pull requests into this fork's `main`; never target upstream with personal port work.
- On each PR, update `CHANGELOG.md`, relevant README sections, tests, and the Windows workflow if build behavior changes. Review CI failures and fix them before merging.
- Prereleases are created from successful manual Windows workflow runs when `prerelease` is selected. Include the license with every download. Check the license's competition and branding terms before distributing a public build.
- Future priorities: robust show editing and MxU library import, stage display, NDI, DeckLink, and production diagnostics. Keep these separate from the first milestone and protect the macOS app from regressions.
