---
paths:
  - "src/Lumen.App/**"
---

# UI and WPF rules (M10; enforced by repo tests where possible)

## Owner's taste

Calm, restrained effects; clear motion; no saturated colors; square-cornered tags (no ovals); blocks aligned and centered (no empty band on one side); related buttons grouped, never at opposite edges. When unsure, make it subtler and name the constant that turns it up. He checks full screen and windowed.

## Look

- **Font:** Exo 2 (SIL OFL), four static weights in `Assets/Fonts`; icons stay Segoe Fluent Icons. No literal font families.
- **Type scale:** `Lumen.FontSize.*` (Label 11, Caption 12, Small 13, Body 14, Lead 15, BodyLarge 16, Subtitle 18, Section 20, Title 24, Headline 32, Display 44) and `Lumen.Text.*`. A literal scale size in a view fails a test.
- **Palette and brand:** base `#0A0D14`, glass cards (`Lumen.Card` = `Lumen.Brush.Glass` ≈ 85 %). Chrome (buttons, sidebar, inputs, focus rings, links, menus) uses the fixed brand `Lumen.Brush.Brand*` (blue → violet); the game's color (`Lumen.Brush.Accent`, `AccentService`) only tints art; glows around art use `Lumen.Brush.AccentGlow` / `AccentPicker.Soften`. Primary actions: `Lumen.Button.Primary`; others `Lumen.Button`, `Lumen.Button.Icon`; tags `Lumen.Badge`, `Lumen.Pill` (6 px corners); on/off `Lumen.Switch`.
- **Surfaces:** window and sidebar use `Lumen.Brush.Backdrop` / `SidebarBackdrop` (translucent only with Mica: Windows 11 22H2+ and "Transparency effects"). Detail and overlays stay opaque; the wizard sits on `Lumen.Brush.Scrim` (95 %). Popups and other windows are never translucent.
- **Room for scaled things:** `VirtualizingStackPanel`/`ScrollViewer` clip to their bounds: Library tiles carry 8 px padding, Home rows use negative margin plus inner margin. The Library grid is centered and its header card sized to the tiles (`OnGridSizeChanged`).

## Motion and performance

- **Motion:** only through `Services/Motion.cs` (`To`, `PageIn`, `PanelIn`, `Stagger`, `FadeIn`, `HoverScale`, `ShowIn`, `Shown`, `Breathe`). Interactions ≤ 250 ms (Fast 140, Normal 220; hero/accent 450). "Animation effects" off = values jump. Covering panels bind `Motion.Shown` (start Collapsed): they hide at once and a picture fades out, added on the next dispatcher turn and never able to crash. `Motion.Breathe` (ambient lights) is the only long-running animation (20 fps, stops when hidden/minimized).
- **Performance:** no per-tile bitmap effects in grids; glow is a brush plus opacity mask, collapsed until needed; animate only the pointed-at tile; heavy pages start their entrance after layout.

## Shell pieces

Top bar search = the Library filter (Ctrl+K/Ctrl+F); toasts via `ToastService` (text as `Func<string>`); sidebar footer = Steam profile from Steam's local files + CPU/RAM monitor (only while visible); quick search groups results and opens a detail with Ctrl+Enter; all menus share `Controls/Menus.xaml`.

## Checklist when adding UI

- **resx:** by script, every key in **both** `Strings.resx` and `Strings.es.resx` (a repo test checks they match and that every `{l:Loc}` key exists; it can't see keys built in code).
- **New views:** every `Views/*.xaml` needs its `.xaml.cs`, or the page renders blank. A `ListBox` needs `Foreground="{DynamicResource Lumen.Brush.Text}"`.
- **New overlays** need a `GamepadLayer` priority (detail = 1, routines preview/editor = 1, Discover = 2, welcome = 3).
- **WPF traps that bit us:** bindings don't see static properties through the DataContext (expose instance properties); never add children to a panel while WPF walks the tree (a page being torn down): defer with the Dispatcher; a local `MenuItem`/`Button` style without `BasedOn` drops the shared look; `System.IO`/`System.Net.Http` aren't implicit in the WPF project.
- UI work cannot be seen from the cloud: say so and hand over the manual test.
