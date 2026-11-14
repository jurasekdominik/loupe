# Loupe

A small Windows tray app for inspecting and aligning desktop and borderless game UI down to a physical screen pixel.

The square lens stays **live**. Pin an area, place guides, measure gaps, and watch the pixels update while adjusting your UI. Integer magnification uses nearest-neighbor sampling, without smoothing between source pixels.

The Windows cursor is excluded from the preview and saved images using Windows Graphics Capture. Mouse guides and pinned rulers remain available. A cursor rendered directly into a game's own frame is part of that image and cannot be removed by this switch; pin the area and move that cursor away to inspect the underlying pixels.

## Run

Open `artifacts/Loupe/Loupe.exe`, or extract `artifacts/Loupe-win-x64.zip` and run the executable from the extracted folder. Keep its neighboring DLLs. Quit an older tray instance before starting the updated build.

Requires Windows 11 or Windows 10 version 2004 or newer, a Direct3D 11 GPU, and **.NET 10 Desktop Runtime (x64)**. No administrator rights are needed. Startup with Windows is optional.

If RTSS / MSI Afterburner draws FPS or sensor statistics over Loupe, add `Loupe.exe` as a separate RTSS application profile and set **Application detection level → None**, then restart Loupe. Keep your WoW profile enabled. Loupe has its own Direct3D presentation surface, which RTSS can otherwise detect.

## Shortcuts

Settings → Shortcuts shows the five main bindings. Click a binding, press and release your chosen keys, then save settings. Escape cancels recording; the × button unassigns an action. Application shortcuts are suspended while settings are open. Reserved keys, duplicate assignments, and registered global shortcut conflicts are reported before saving.

| Action | Default |
| --- | --- |
| Show / hide | **F10** |
| Zoom in / out | **Ctrl + Alt + Plus / Minus** |
| Pin / follow source | **Ctrl + Alt + L** |
| Measurement mode | **Ctrl + Alt + M** |

**Additional shortcuts → Show additional** reveals the optional actions: enable/disable, hold to peek, dismiss, clear, grid, guides, ruler points, panning, lens size, screenshots, settings, captures folder and exit. These are unassigned by default; the toolbar, mouse controls and tray remain available. Hold to peek accepts modifiers alone. Guide, ruler-point and pan bindings act only inside the interactive lens. Upgrading from version 0.3.1 clears the former default assignments for these optional actions while preserving custom assignments and explicitly unassigned actions.

## Inspect and measure

1. Point at the area to inspect and show the loupe. Its location stays fixed, with the source following your pointer. Following pauses over the lens, toolbar and capture menu.
2. **Pin** fixes the sampled area while keeping the image live. Ordinary viewing passes mouse clicks through to the application underneath. Showing the loupe does not take keyboard focus.
3. **Measure** pins the area and makes its enlarged pixels interactive. Crosshair guides snap to source pixel boundaries.
4. **Left-drag** to measure width and height, or use the ruler-point binding twice. A three-pixel gap reads **3 px** at every zoom level.
5. **Right-click** to pin both crosshair guides; use the horizontal/vertical bindings for one guide. Repeat at the same boundary to remove it.
6. **Middle-drag**, or use the pan bindings, to move the sampled area. Guides and ruler endpoints retain their original screen coordinates.
7. Leave measurement mode to pass clicks through again. Guides remain until cleared, including across hiding and reopening the lens; they reset when Loupe exits or the monitor changes. The ruler starts fresh on each reopen.

The right side of the toolbar's header shows, in blue, the X/Y of the pixel at the crosshair while following or pinned, the boundary under the pointer while measuring, and the ruler's width and height once one is placed.

The wheel zooms inside the interactive lens; the toolbar also has zoom buttons. Drag the toolbar's header row to move the lens and controls together. Lens-size commands expand or shrink the inspection area.

## Capture images

The toolbar's **Capture** menu and recorded shortcuts offer:

- **Original area:** source pixels at their original resolution, with no loupe guides or scaling.
- **Magnified view:** the enlarged view, including its grid, crosshair, pinned guides and ruler.

Both save lossless PNGs to the Windows Pictures folder's `Loupe` subfolder by default. Choose a different folder in Settings → General. Images are also copied to the clipboard unless disabled. A busy clipboard does not prevent the PNG from being saved. Opening the captures folder is available in the tray and as an assignable action.

Captures happen only on request. Live inspection does not save images or read frame pixels back to the CPU.

## Appearance

The dark UI uses **Sky Blue `#0593C6`**, **White `#FCFFFF`**, and **Hot Pink `#D83995`**. Blue identifies actions and the live lens; pink identifies pinned guides, rulers, and recording. Neutral Shade surfaces, single thin borders, subtle active tabs, and color-only focus keep controls consistent. Native title-bar colors match the dark surface on supported Windows versions.

[Shade](https://jurasekdominik.github.io/shade/) is the visual reference for spacing, attached dropdown menus, corners, and border focus treatment. These controls are drawn locally in WinForms; the app does not embed a browser or require Shade at runtime.

Settings has three pages: **Lens**, **Shortcuts**, and **General**. General contains startup and screenshot preferences. The supplied blue glass lens icon is used throughout the executable, windows and tray. Its multi-resolution ICO includes individually drawn sizes from 16 to 256 px, selected to suit the window or tray. The same `src/Loupe/loupe.ico` supplies the executable icon and embedded window/tray resource; the portable package includes `loupe.ico` for Windows shortcuts. The editable vector master is `assets/icons/loupe.svg`. Previous icon alternatives and the icon selector have been removed.

## Monitors and scaling

One monitor is selected automatically. With several displays, the first activation asks which monitor to capture, then remembers it. Change that choice in Settings → Lens. A display topology change stops capture; reopen the loupe to reconnect or choose a display.

Controls and lens size follow per-monitor DPI. Source pixels, grid boundaries, guides, ruler points and original captures stay in physical screen coordinates. Zoom levels are 2×, 4×, 6×, 8×, 12×, 16×, 24× and 32×. Measurements use hardware pixels, independently of WoW's UI coordinate system.

The on-screen GPU checks run at 100% scaling. Coordinate tests cover simulated scaling from 100% to 400% and negative monitor origins. Windows Graphics Capture supplies upright monitor frames. Actual mixed-DPI, multi-monitor and rotated-display hardware still needs validation. The initial target is SDR borderless/windowed desktop applications; HDR color accuracy is not validated.

## Performance and preferences

Windows Graphics Capture, cached frames, cropping and nearest-neighbor enlargement stay on the GPU. Visible refresh is capped at 30 or 60 fps, with 60 as the default. Each new frame is drawn straight from the capture buffer, without copying the monitor image. The capture session also receives that interval on Windows versions which support it. Source update rate and scheduling affect actual refresh; unchanged frames do not need to be redrawn.

Opening prepares the first captured GPU frame while the lens is hidden, then reveals the lens and controls together. A quick second toggle cancels opening. Hiding the lens stops its timer and releases capture/graphics resources, including the native capture session and frame pool. With the five default bindings, no background keyboard hook is needed; optional peek and pointer commands enable one when assigned. Disabling shortcuts removes that listener; the tray and any assigned always-available actions remain. Idle work is event-driven; constant zero usage and in-game frame rates are not guaranteed.

Preferences and a bounded error log live in `%LOCALAPPDATA%\Loupe`. First-build preferences migrate to the new binding format automatically; the old circle-lens setting is discarded. This implementation has no telemetry, updater or application network requests.

## Build and test locally

Use the .NET 10 SDK. On a new machine, a first dependency restore needs NuGet access:

```powershell
dotnet restore Loupe.slnx --locked-mode
```

Once dependencies are cached, the scripts below use only the local cache, with network auditing disabled for offline restore:

```powershell
./scripts/Test-Local.ps1
./scripts/Test-Local.ps1 -Render
./scripts/Test-Local.ps1 -Render -Ui
./scripts/Publish-Local.ps1
```

Local checks exercise measurement math, DPI mapping, boundaries, settings migration/recovery, recorded chords and release, modifier-only peek, cancellation, reserved/duplicate shortcuts, thin border painting and lossless PNG saving. They do not change the system clipboard or inject keyboard input into other apps.

`-Render` briefly shows a synthetic one-pixel chart. It checks exact 8×/12× GPU enlargement, live updates, self-capture exclusion, a 17×9-pixel ruler, original/magnified PNGs, keyboard focus and resource reopening. Settings and rendering previews are saved under `artifacts/smoke/` in this explicit diagnostic mode.

`-Ui` checks the real popup HWND's attachment, sends owner mouse messages through Loupe's own message loop to check close/reopen behavior, verifies control bounds and scrolling, and switches all three pages. It checks cursor-exclusion readback and, when the current pointer is visible, the exact pixels beneath it on a synthetic surface. If the pointer moves away or another app hides it, that pixel check is reported as skipped. It never moves the pointer. Native window previews go to `artifacts/ui/`. Additional 125%–200% layout stress checks resize the controls; they do not replace real DPI testing. This separate diagnostic process permits printing its own windows; normal capture exclusion remains covered by `-Render`.

The settings dialog uses a compact 620 × 480 layout at 100% scaling, with 28 px controls. Pages prepare their controls once and reuse measured geometry. UI diagnostics also record switching times and table-layout counts in `artifacts/ui/switching.json` and check that cached pages fill the available width after switching.

**GitHub Actions are not configured or used.**

## Implementation

C# / .NET 10, WinForms, Windows Graphics Capture, Direct3D 11, Direct2D, and DirectComposition, using Vortice bindings and a small native WinRT capture bridge. No additional runtime package is required for cursor exclusion. Toggle shortcuts use Windows registration; event-driven keyboard hooks handle held peek, in-lens commands and recording. No game injection, game memory access or game input automation is used.

Pinpoint, ZoomLens and ZoomIt provided interaction references. Their source code is not incorporated. Dependency licenses are in `THIRD-PARTY-NOTICES.txt`.
