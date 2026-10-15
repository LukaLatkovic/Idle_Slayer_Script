# Idle Runner C# Migration

This branch contains the C# rewrite of the AutoIt application. The original AutoIt files remain available as the behavioral reference.

## Current scope

The first vertical slice provides:

- a .NET 10 WPF desktop application;
- an automation core that is independent of Windows and UI code;
- a Windows adapter that locates the Idle Slayer window and emits left-click jump and right-click boost pulses through `SendInput`;
- explicit game activation when the user starts automation;
- enforced foreground-window and 1280 x 720 client-area safety gates;
- an explicit stopped-by-default Start/Stop state;
- a live count of input pulses confirmed by the Windows adapter;
- persisted interval settings and local activity logs;
- client-only game frame capture with a lighting-tolerant grayscale template matcher;
- named visual observations that combine multiple independently scored anchors;
- optional persisted gameplay-HUD calibration that blocks input when the normal HUD is not recognized;
- a Silver Box collector that supports both the original color marker and optional shape calibration;
- a tested Chest Hunt decision engine covering the normal, perfect, and missing-saver strategies;
- automatic Chest Hunt execution, including click retries, partial-grid continuation, Saver strategy, Mimic and 2x handling, and result-screen closing;
- automatic Bonus Stage 2 and Bonus Stage 3 execution, including both Spirit Boost routes, opening-slider handling, section retries, loot collection, failure closing, and the Skip Bonus Stage option;
- automatic upgrades and equipment purchasing, including the original `Ctrl+Shift+B` run-now shortcut;
- automatic ascension, quest claiming, minion collection, portal cycling, Mega Horde rage, and configured temporary-item crafting;
- Ascending Heights, Victor, and Knight boss-fight automation based on the original routes;
- the complete legacy General, Minigames, and Crafting configuration model;
- one-time import from a legacy `IdleRunnerLogs/Settings.txt` file when upgrading;
- dependency-free executable tests for the core safety behavior.

The visual stack provides typed BGRA frames and normalized luminance template matching for calibrated modules. The Bonus Stage compatibility port also preserves the original fixed-coordinate color markers because its movement routes are tied to the legacy 1280 x 720 layout. Every wait now has a timeout so a missing marker cannot block the runner forever. An optional direct game-state adapter can still be added behind the same platform boundary.

## Projects

| Project | Purpose |
| --- | --- |
| `IdleRunner.Core` | Automation rules and adapter contracts. Targets .NET Standard 2.1 so it can be shared with a future .NET 6 mod adapter. |
| `IdleRunner.Platform.Windows` | Idle Slayer window discovery and guarded Win32 mouse input. |
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
Configuration changes are saved immediately. All options inherited from the original application are connected to an automation executor.

Chest Hunt strategy and Lockpicking timing are connected to the calibrated 10 x 3 grid. The runner recognizes closed chests by shape, retries ignored clicks, classifies Mimic and 2x result signatures, opens the Saver according to the configured strategy, and closes the result screen. It can continue a partially opened grid by skipping opened cells and using the original application's no-Mimic fallback state when earlier outcomes are unavailable.

Bonus Stage automation recognizes the legacy entrance, clears any opening slider, determines stage 2 or 3 and Spirit Boost state, then runs every section from `BonusStage.au3` with the original hold and wait timings. Jump commands use direct left-mouse holds because the current Unity build ignores synthetic keyboard commands. If **Skip bonus stage** is enabled, the runner waits for the failure result and closes it instead of starting the route.

## Safety behavior

The application activates Idle Slayer only as a direct response to the user pressing **Start automation**. It does not repeatedly steal focus. Mouse input is emitted only when all of these conditions are true:

1. automation was explicitly started;
2. a visible, non-minimized window titled `Idle Slayer` exists;
3. that window is currently in the foreground;
4. its client area is exactly 1280 x 720.

The **Calibrate gameplay screen** action captures the normal gameplay HUD while menus are closed. Once calibrated, at least one of the two bottom action-slot anchors must remain visible before a normal gameplay pulse can be sent. Calibration is optional and does not change the Start/Stop state.

The Silver Box collector recognizes the original top-window gold marker and clicks the original `(644, 49)` client position. Its optional calibration adds shape matching but is no longer required.

The Chest Hunt calibration in **Minigames** must be run while the yellow Saver marker and multiple closed chests are visible. It records both the Saver and the dominant closed-chest shape and verifies that exactly one Saver matches. Calibration itself never opens a chest and does not change the Start/Stop state.

Bonus Stage automation requires the same 1280 x 720 client area as the legacy routes. Its entrance markers now use bounded regions and color tolerance instead of a single exact pixel. A failed stage or missing marker ends only that attempt and never stops the main automation.

The Windows adapter checks foreground ownership again immediately before emitting input.
Each jump-and-boost command holds both mouse buttons for 35 milliseconds so the Unity input loop can observe the pressed state before they are released. Targeted UI, Chest Hunt, and Bonus Stage input uses the legacy `mouse_event` path and leaves the cursor on the target until the next action because the installed Unity 6000.3 build evaluates the cursor position on a later frame. Keyboard `SendInput` and direct window messages were tested and ignored by the game.

Because Unity evaluates the real cursor location for mouse input, the Windows adapter moves the cursor only for the duration of a serialized command. It restores the saved user position immediately afterward; if the user moved the mouse during the command, the user's new position is preserved. Losing foreground temporarily suspends timers and input, and returning to Idle Slayer resumes automatically without another click.

## Next migration slices

1. Capture and approve named visual rules for specific Idle Slayer states.
2. Add captured regression fixtures for every observation rule.
3. Prototype an optional MelonLoader adapter for direct state and actions, pinned to a verified Idle Slayer version.
4. Keep the external Windows adapter as the fallback when the internal game interface changes.

Direct integration is deliberately deferred until a local game installation and its generated IL2CPP types can be inspected. Community mods demonstrate that this approach is possible, but their APIs are version-sensitive and are not an official Idle Slayer automation interface.
