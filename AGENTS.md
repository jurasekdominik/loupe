# Working on Loupe

- This is a native Windows C# / .NET 10 desktop app, with WinForms for the tray and settings and Direct3D/Direct2D/DirectComposition for the live lens.
- All tests run locally. Do not add, enable, dispatch, or use GitHub Actions unless the user explicitly lifts this restriction. The user has exhausted their current Actions quota.
- Run `./scripts/Test-Local.ps1` for coordinate/settings checks, add `-Render` for a brief on-screen synthetic pixel check and `-Ui` for settings interactions. Restore uses only the existing local NuGet cache.
- Keep image, guide and ruler coordinates in physical desktop pixels. UI chrome can scale with DPI; source pixels must retain exact integer enlargement.
- Capture through standard Windows desktop APIs. Do not add game injection, game memory access, or input automation.
- Hidden/disabled state must stop capture and rendering; preserve the tray and its event-driven shortcuts.
- Windows Graphics Capture excludes the actual Windows cursor. Keep capture and enlargement on the GPU. Never hide the system cursor or reconstruct pixels behind a game-rendered cursor.
- Keep action names, scopes and defaults in `CommandCatalog`. Settings use a key recorder, with duplicate/reserved-key validation; suspend application bindings while settings are open.
- Show only the five main shortcut recorders initially: show/hide, zoom in/out, pin and measure. Other actions are optional and unassigned by default; preserve custom bindings during migration.
- Keep the lens and toolbar hidden until the first captured frame has been presented. Opening must remain cancellable; initialize capture on the next UI tick and stop it when hidden.
- The only lens shape is square. Blue `#0593C6`, white `#FCFFFF`, and pink `#D83995` are the UI accents. Shade is a visual reference, implemented with local controls rather than a web view.
- Use the supplied `src/Loupe/loupe.ico` for the executable and embedded window/tray icons, with its native size-specific frames. `assets/icons/loupe.svg` is the editable master. Keep a single icon design; do not restore generated alternatives or an icon selector.
- Settings have Lens, Shortcuts and General pages. Keep the dialog compact with 28 px controls, thin borders, color-only focus and subdued navigation. Cache layout constraints and batch page visibility changes; avoid decorative settings rows and custom widgets when existing controls suffice.
- Read screen pixels back to the CPU only on explicit screenshot requests or in local rendering checks. Tests must not change the system clipboard or inject keyboard input into other apps.
