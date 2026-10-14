# Idle Runner C# Migration

This branch contains the beginning of a staged rewrite of the AutoIt application. The original AutoIt files remain available while behavior is moved into testable C# components.

## Current scope

The first vertical slice provides:

- a .NET 10 WPF desktop application;
- an automation core that is independent of Windows and UI code;
- a Windows adapter that locates the Idle Slayer window and emits `Up` and `Right` through `SendInput`;
- enforced foreground-window and 1280 x 720 client-area safety gates;
- a paused-by-default startup state;
- persisted interval settings and local activity logs;
- dependency-free executable tests for the core safety behavior.

This version intentionally does not copy the legacy pixel-color decision logic. It establishes a safe execution path first. Pixel observation and an optional direct game-state adapter can be added behind the same `IGameAdapter` boundary.

## Projects

| Project | Purpose |
| --- | --- |
| `IdleRunner.Core` | Automation rules and adapter contracts. Targets .NET Standard 2.1 so it can be shared with a future .NET 6 mod adapter. |
| `IdleRunner.Platform.Windows` | Idle Slayer window discovery and guarded Win32 keyboard input. |
| `IdleRunner.Desktop` | WPF control surface, settings persistence, and logging. |
| `IdleRunner.Core.Tests` | Small executable test suite with no external test packages. |

## Build and run

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet build IdleRunner.slnx
dotnet run --project tests/IdleRunner.Core.Tests
dotnet run --project src/IdleRunner.Desktop
```

The desktop application stores settings and logs under `%LOCALAPPDATA%\IdleRunner`.

## Safety behavior

The application never activates Idle Slayer on the user's behalf. Keyboard input is emitted only when all of these conditions are true:

1. automation was explicitly started;
2. a visible, non-minimized window titled `Idle Slayer` exists;
3. that window is currently in the foreground;
4. its client area is exactly 1280 x 720.

The Windows adapter checks foreground ownership again immediately before calling `SendInput`.

## Next migration slices

1. Add frame capture and typed observations without embedding decisions in pixel-reading code.
2. Port bonus-stage, chest-hunt, and ascending-heights behavior one module at a time.
3. Add replayable screenshots and regression tests for every observation rule.
4. Prototype an optional MelonLoader adapter for direct state and actions, pinned to a verified Idle Slayer version.
5. Keep the external Windows adapter as the fallback when the internal game interface changes.

Direct integration is deliberately deferred until a local game installation and its generated IL2CPP types can be inspected. Community mods demonstrate that this approach is possible, but their APIs are version-sensitive and are not an official Idle Slayer automation interface.
