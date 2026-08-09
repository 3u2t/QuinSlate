# SPEC: Core buffers, tray icon, hotkey, autosave

> _Last updated: 2026-08-08_

## What
The foundation of the app. Five persistent text buffers displayed in a
floating panel, a system tray icon, a global show/hide hotkey, and
automatic saving to plain text files.

## Behaviour

- On a manual launch the panel is shown. When launched automatically by the
  Windows startup task at login, the app appears only as a tray icon and no
  window is shown. (The launch kind is read via
  `AppInstance.GetActivatedEventArgs` — a `StartupTask` kind means a login launch.)
- A global hotkey (Ctrl+Shift+Q) toggles the panel visible/hidden. If the panel is visible but not the active foreground window, it brings the panel to the foreground instead of hiding it. If the window has been minimized (e.g. by "Show Desktop"/Win+D), the hotkey and tray click restore it rather than toggling — `AppWindow.Show()`/`SetForegroundWindow` alone do not un-minimize a window, so the panel is explicitly restored (`OverlappedPresenter.Restore`).
- The panel contains 5 tabs. (Originally numbered 1–5 with a distinct colour each;
  superseded by the emoji + editable-title tab design — see
  [14-TABS-REDESIGN.md](14-TABS-REDESIGN.md).)
- Each tab holds a plain multiline editor. No formatting controls. (All five buffers are
  edited in one WebView2 hosting CodeMirror 6 — one CM6 state per buffer — which preserves
  per-buffer undo across tab switches and keeps programmatic edits, e.g. the inline calc, as
  clean undo steps. See [11-INLINE-CALC.md](11-INLINE-CALC.md) and the migration spec
  [17-EDITOR-CODEMIRROR-MIGRATION.md](17-EDITOR-CODEMIRROR-MIGRATION.md). Earlier builds used a
  `RichEditBox`.)
- Text is saved automatically. There is no save button.
- On next launch all buffer content is restored to the state at last write.

## Files

Buffers persist as UTF-8 text files (with BOM):

    %AppData%\QuinSlate\buffer-1.txt
    %AppData%\QuinSlate\buffer-2.txt
    ...
    %AppData%\QuinSlate\buffer-5.txt

The directory is created on first launch if it does not exist.
A missing buffer file on startup is treated as an empty buffer — no error.

## Autosave

Write is triggered on a 300 ms debounce after the last keystroke in a
buffer. Do not write on every keystroke. On application exit, flush any
pending write synchronously before the process ends.

## Tray icon

Implemented via Win32 `Shell_NotifyIcon` (P/Invoke). WinUI 3 has no
native tray API.

- Icon must be removed with `NIM_DELETE` before the window is destroyed
  to prevent a ghost icon persisting in the taskbar until Explorer restarts.
- Left-click on the tray icon toggles the panel (same as the hotkey).

## Hotkey

Registered with `RegisterHotKey` (Win32). Hardcoded to Ctrl+Shift+Q
(`MOD_CONTROL | MOD_SHIFT`, `VK_Q`). If registration fails (conflict), log
the error. Do not crash or show a dialog.

## Panel

- Default size: 740 × 560 logical px (device-independent pixels at 96 DPI = 1 DIP). The width
  is set by the tab strip, not by taste: below 719 DIP the five tabs no longer fit at their
  100 DIP floor and the strip surfaces scroll chevrons, so the default sits just above that
  (see [14-TABS-REDESIGN.md](14-TABS-REDESIGN.md)).
- Minimum size: 360 × 280 logical px, enforced via `WM_GETMINMAXINFO`.
- Resizable. The user may drag the window edges to any size above the minimum.
- Font: system monospace, 13 pt.

## Window chrome

The system title bar and its caption buttons are removed
(`OverlappedPresenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false)`); the resize
border stays. The panel draws its own four title-bar buttons at the top right, left to right:

| Button | Action |
|---|---|
| Pin | Toggles always-on-top — see [09-PIN-WINDOW.md](09-PIN-WINDOW.md) |
| Minimize | Minimizes the window to the taskbar. It stays a taskbar window; it does **not** hide to the tray |
| Maximize / Restore | Toggles maximized. The glyph tracks the live window state, so shell gestures that maximize without the button (Win+Up, snap layouts, dragging to the top edge) keep it correct |
| Close | Hides the panel to the tray. The app keeps running |

Minimize is the one place where "minimize" does not mean "hide to tray". Every *other* route
into minimize still hides to the tray — notably a click on the taskbar button while the panel
is active, which the shell delivers as `WM_SYSCOMMAND`/`SC_MINIMIZE`, and "Show Desktop"
(Win+D), which delivers `WM_SIZE`/`SIZE_MINIMIZED`. `MainWindow` intercepts both and calls
`HidePanel`; the panel's own minimize button raises a flag for the duration of its
`OverlappedPresenter.Minimize()` call so that interception stands aside.

## Out of scope

Hotkey configuration, custom colours, font settings.
