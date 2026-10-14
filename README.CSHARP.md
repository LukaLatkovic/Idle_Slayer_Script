# Idle Runner C# Migration

This branch contains the beginning of a staged rewrite of the AutoIt application. The original AutoIt files remain available while behavior is moved into testable C# components.

## Current scope

The first vertical slice provides:

- a .NET 10 WPF desktop application;
- an automation core that is independent of Windows and UI code;
- a Windows adapter that locates the Idle Slayer window and emits left-click jump and right-click boost pulses through `SendInput`;
- explicit game activation when the user starts automation;
- enforced foreground-window and 1280 x 720 client-area safety gates;
- a paused-by-default startup state;
- a live count of input pulses confirmed by the Windows adapter;
- persisted interval settings and local activity logs;
- client-only game frame capture with a lighting-tolerant grayscale template matcher;
- named visual observations that combine multiple independently scored anchors;
- optional persisted gameplay-HUD calibration that blocks input when the normal HUD is not recognized;
- a shape-calibrated Silver Box collector that refuses to click when the claim icon is absent;
- a tested Chest Hunt decision engine covering the normal, perfect, and missing-saver strategies;
- Chest Hunt grid and Saver calibration against a live 1280 x 720 game frame;
- the complete legacy General, Minigames, and Crafting configuration model;
- one-time import from a legacy `IdleRunnerLogs/Settings.txt` file when upgrading;
- dependency-free executable tests for the core safety behavior.

This version intentionally does not copy the legacy single-pixel decision logic. It now provides typed BGRA frames and normalized luminance template matching so visual rules can use shapes and local contrast instead of one exact RGB value. An optional direct game-state adapter can still be added behind the same platform boundary.

## Projects

| Project | Purpose |
| --- | --- |
| `IdleRunner.Core` | Automation rules and adapter contracts. Targets .NET Standard 2.1 so it can be shared with a future .NET 6 mod adapter. |
| `IdleRunner.Platform.Windows` | Idle Slayer window discovery and guarded Win32 keyboard input. |
| `IdleRunner.Desktop` | WPF control surface, settings persistence, and logging. |
| `IdleRunner.Core.Tests` | Small executable test suite with no external test packages. |
| `IdleRunner.InputProbe` | One-shot Windows input diagnostics and repeatable BMP frame capture against a running game build. |

## Build and run

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet build IdleRunner.slnx
dotnet run --project tests/IdleRunner.Core.Tests
dotnet run --project src/IdleRunner.Desktop
```

The input probe is a development tool and is not required for normal application use.

To capture the current game client for observation development, run:

```powershell
dotnet run --project tools/IdleRunner.InputProbe -- capture artifacts/gameplay.bmp
```

The desktop application stores settings, visual calibration, and logs under `%LOCALAPPDATA%\IdleRunner`.
Configuration changes are saved immediately. Options for modules that have not yet been ported are marked `MODULE PENDING` in the desktop application.

Chest Hunt strategy and Lockpicking timing are implemented independently from screen control. The Minigames page can calibrate the 10 x 3 chest grid and locate the highlighted Saver from its complete local shape. Guarded chest execution remains disabled until opened-chest and outcome observations are captured and tested.

## Safety behavior

The application activates Idle Slayer only as a direct response to the user pressing **Start automation**. It does not repeatedly steal focus. Keyboard input is emitted only when all of these conditions are true:

1. automation was explicitly started;
2. a visible, non-minimized window titled `Idle Slayer` exists;
3. that window is currently in the foreground;
4. its client area is exactly 1280 x 720.

The **Calibrate gameplay screen** action captures the normal gameplay HUD while menus are closed. Once calibrated, at least one of the two bottom action-slot anchors must remain visible before an input pulse can be sent. Calibration is optional and always leaves automation paused.

The Silver Box collector has a separate calibration in **General**. Calibration is accepted only while the bright, high-contrast claim icon is visible in its expected top-center position. The collector then checks the complete icon shape in a bounded search area before clicking; a plain background or the remaining-box counter is rejected.

The Chest Hunt calibration in **Minigames** must be run while the yellow Saver marker is visible. It records the grid layout and Saver appearance, verifies that exactly one grid cell matches, and always leaves automation paused. Calibration itself never opens a chest.

The Windows adapter checks foreground ownership again immediately before calling `SendInput`.
Each jump-and-boost command holds both mouse buttons for 35 milliseconds so the Unity input loop can observe the pressed state before they are released. Keyboard `SendInput` and direct window messages were tested against the installed Unity 6000.3 build and were ignored by the game; mouse input was confirmed from captured game frames.

Because Unity evaluates the real cursor location for mouse input, the Windows adapter saves the cursor position and parks it at the center of the game client area while automation is active. It restores the original position when automation is paused, the game loses foreground focus, or the application exits normally.

## Next migration slices

1. Capture and approve named visual rules for specific Idle Slayer states.
2. Add opened-chest and result observations, then connect the tested Chest Hunt strategy to guarded clicks.
3. Add captured regression fixtures for every observation rule.
4. Prototype an optional MelonLoader adapter for direct state and actions, pinned to a verified Idle Slayer version.
5. Keep the external Windows adapter as the fallback when the internal game interface changes.

Direct integration is deliberately deferred until a local game installation and its generated IL2CPP types can be inspected. Community mods demonstrate that this approach is possible, but their APIs are version-sensitive and are not an official Idle Slayer automation interface.
