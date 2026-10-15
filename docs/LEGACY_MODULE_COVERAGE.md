# Legacy module coverage

This document maps the AutoIt implementation to the C# automation runtime. The original files remain the timing and coordinate reference. All fixed-coordinate behavior assumes an English, windowed 1280 x 720 Idle Slayer client at 100% display scaling.

## Main event loop

| AutoIt behavior | C# implementation | Trigger |
| --- | --- | --- |
| Jump and boost thread | `AutomationEngine` and `JumpAndBoostModule` | Configured jump interval |
| Silver Box collection | `LegacyGameAutomationService` | Original gold marker or optional calibrated icon |
| Mega Horde rage | `LegacyGameAutomationService` | Mega Horde marker |
| Soul Bonus rage | `LegacyGameAutomationService` | Soul Bonus marker, debounced |
| Temporary crafting items | `LegacyGameAutomationService` | Mega Horde and configured crafting options |
| Quest claiming | `LegacyGameAutomationService` | Completed-quest marker |
| Minion collection | `LegacyGameAutomationService` | Ready-minion marker |
| Chest Hunt | `ChestHuntAutomationService` | Calibrated Chest Hunt grid |
| Bonus Stage 2 and 3 | `BonusStageAutomationService` | Tolerant two-marker entrance detection |
| Victor and Knight boss fights | `LegacyGameAutomationService` | Three boss entrance markers |
| Ascending Heights | `LegacyGameAutomationService` | Two Ascending Heights entrance markers |
| Portal cycling | `LegacyGameAutomationService` | Ready portal and enabled setting |
| Upgrade/equipment purchasing | `LegacyGameAutomationService` | Timer or `Ctrl+Shift+B` |
| Auto ascend | `LegacyGameAutomationService` | Timer and enabled setting |
| Generic result closing | `LegacyGameAutomationService` | Red result marker |

## Library mapping

- `BonusStage.au3`: all Stage 2, Stage 2 Spirit Boost, Stage 3, and Stage 3 Spirit Boost sections, slider directions, collection loops, retry/failure checks, wall jumps, and Skip Bonus Stage behavior are present.
- `ChestHunt.au3`: normal, perfect, no-Lockpicking, and no-Reinforced-Crystal-Saver strategies are present. The C# implementation also retries ignored Unity clicks and can resume a partially opened grid.
- `AscendingHeights.au3`: slider handling, player/platform tracking, steering, win/failure handling, retry click, and the four-minute limit are present.
- `BossFightVictor.au3`: slider handling, continuous shooting, normal/flame attack responses, stage transition, win closing, and timeout are present.
- `BossFightKnight.au3`: the route below the original function's unconditional `Return` is enabled in C#, including opening shots, close/ranged reactions, stage transition, dark stage, win closing, and timeout.
- `Common.au3`: color search, bounded waits, slider movement, and held jump commands are implemented with captured client frames and guarded native input.
- `GUI.au3`: all 16 persisted settings are represented by `AutomationSettings`; the original auto-buy nine-second first run is preserved. Start/Pause is intentionally replaced by explicit Start/Stop.
- `AutoThreadV3.au3`: file-based worker processes are replaced by cancellable .NET tasks and an exclusive-action coordinator.
- `Log.au3`: activity messages are available in the selectable/copyable Activity view and the local application log.
- `ResourcesEx.au3`: embedded AutoIt GUI resource plumbing has no runtime equivalent because WPF owns the C# interface and resources.

## Runtime rules

- Only the user can change the application from running to stopped, through the main button or the Home shortcut.
- Losing foreground does not change the Start/Stop state. Input and active sequence timers wait until Idle Slayer becomes foreground again, then resume automatically.
- Unexpected detector, capture, or input failures end only the current module attempt and are written to Activity. They do not stop the main automation.
- Mouse operations are serialized. The cursor is moved only for a command and restored afterward. If the user moves it during a command, the user's new position is preserved.
- Long or formerly unbounded AutoIt searches have finite iteration or time limits so one stale screen cannot block every other module permanently.

## Original game prerequisites

The original route assumptions still apply: Bonus Stage 2 requires the compatible mode and Protect; portal dialogue should be disabled; rounded bulk and hidden locked quest rewards should be enabled; Leadership Master is required for the minion flow; Vertical Magnet should be avoided; and the documented character/weapon requirements apply to Ascending Heights and boss fights.
