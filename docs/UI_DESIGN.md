# Idle Runner Desktop UI Direction

## Visual thesis

The desktop application should feel like a restrained operations console rather than a game overlay. It uses a dark neutral canvas, one lime accent, strong typography, thin dividers, and very little decorative chrome. The interface should remain readable beside the game at a compact window size.

## Content plan

The primary workspace contains the game connection state, automation state, one Start/Pause action, and the jump interval. Secondary context contains the enforced safety gates and a short live activity log. Advanced automation modules will be added only after their behavior exists in the C# core.

## Interaction thesis

The application always starts paused. It sends input only when Idle Slayer is the foreground window and its client area matches the configured reference size. A subtle entrance transition establishes hierarchy, while a restrained status pulse is used only when automation is active. Status changes and blocked actions are written in plain English.
