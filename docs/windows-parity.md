# Windows parity with the Mac app

The goal is the same church presentation workflow and visual hierarchy on Windows, under the **Slides** name. The Mac application is the behavior reference. Keep the PolyForm Shield license and required MxU notice with every build.

| Area | Current Windows implementation | Mac parity still needed |
| --- | --- | --- |
| Workspace | Service Flow with a date picker and section labels, persistent local library, slide grid, output preview, projection controls | Additional Mac panels, saved layouts, accessibility polish |
| Service flow | Add a library item more than once, reorder and remove service entries; saved service date, Pre-Service/Service/Post-Service labels, per-occurrence operator notes and planned minutes, total planned duration; session-only start/pause/reset live timer with item elapsed and remaining time | Linked plans, transitions, arrangements, persistent run history and advanced service tracking |
| Library | Persistent local presentation list and search across shows; folders with create, rename, delete, and move; import from opened `.pshow`; manual media relinking by unique filename | Indexing, full media management, migration from Mac documents |
| Slides | Song sections, text thumbnails, local images and video; slide deck editor; positioned text, solid rectangles, ellipses, and local images with drag, resize, rotation, opacity, flipping, numeric geometry and stacking; image fit, fill, and stretch; per-slide typography and four Mac-inspired starter palettes | Other Mac object types, exact Mac typography and theme layouts, animations, overlays, advanced media layers |
| Output | Fullscreen audience window on a selected display; blackout; preview and projection scale the same 16:9 canvas; slide navigation crosses service item boundaries | Screen layouts and presets, stage displays, NDI, DeckLink, streaming |
| Import | Standalone MxU Presentation JSON text, solid background, basic text style, supported positioned text, solid rectangles and ellipses with rotation, opacity, and flips; portable `.slidespack` import/export with verified embedded media including positioned images | Mac library documents and media IDs, advanced objects and effects, PPTX, ProPresenter, direct Mac document portability |
| Operations | Autosave, portable `.pshow`, Windows CI artifact | Settings, backups, diagnostics, local API, signed installer |

## Development order

1. Continue matching the Mac document and library behavior, including native Mac library import and richer media management.
2. Continue porting scene and theme behavior needed for visually matching slides, with screenshot comparisons at 16:9 and common output resolutions. Starter palettes are only the first step.
3. Complete service editing and controls, then add stage and production outputs.
4. Validate each milestone on a Windows PC using the GitHub Actions download. Keep the Mac build green and document any Mac-to-Windows behavior differences until they are closed.

Do not label a Windows milestone as full parity while any row above remains incomplete. Keep each change reviewable in a focused pull request.
