# Windows parity with the Mac app

The goal is the same church presentation workflow and visual hierarchy on Windows, under the **Slides** name. The Mac application is the behavior reference. Keep the PolyForm Shield license and required MxU notice with every build.

| Area | Current Windows implementation | Mac parity still needed |
| --- | --- | --- |
| Workspace | Service Flow, show-scoped library, slide grid, output preview, projection controls | Service dates, grouping, panels, saved layouts, accessibility polish |
| Service flow | Add a library item more than once, reorder and remove service entries | Linked plans, item timings, transitions, notes, arrangements, service tracking |
| Library | Search presentations in the current `.pshow` | Persistent library and folders, indexing, media management, migration from Mac documents |
| Slides | Song sections, text thumbnails, local images and video | Object editor, themes, typography, animations, overlays, advanced media layers |
| Output | Fullscreen audience window on a selected display; blackout | Screen layouts and presets, stage displays, NDI, DeckLink, streaming |
| Import | Standalone MxU Presentation JSON text subset | Mac library documents, PPTX, ProPresenter, media relinking |
| Operations | Autosave, portable `.pshow`, Windows CI artifact | Settings, backups, diagnostics, local API, signed installer |

## Development order

1. Match the Mac document and library behavior, with backward-compatible show migration and portable media references.
2. Port the scene and theme behavior needed for visually matching slides, with screenshot comparisons at 16:9 and common output resolutions.
3. Complete service editing and controls, then add stage and production outputs.
4. Validate each milestone on a Windows PC using the GitHub Actions download. Keep the Mac build green and document any Mac-to-Windows behavior differences until they are closed.

Do not label a Windows milestone as full parity while any row above remains incomplete. Keep each change reviewable in a focused pull request.
