# Gluttony Combo — Changelog

## v1.0.4.13 (2026-05-18)

### Fixed
- **AutoDuty IPC Integration**: Added AutoDuty IPC subscriber to detect when AutoDuty has paused for mechanics (Pyretic, Untarget, etc.). Gluttony now yields autorotation and target acquisition when AutoDuty is in control, preventing the targeting loop where AutoDuty clears the target and Gluttony immediately retargets.
  - New file: `GluttonyCombo/Services/IPC_Subscriber/AutoDuty.cs`
  - Patched: `GluttonyCombo/GluttonyCombo.cs` — initializes and disposes AutoDuty IPC
  - Patched: `GluttonyCombo/AutoRotation/AutoRotationController.cs` — checks `AutoDutyIPC.ShouldYield` in `ShouldSkipAutorotation()`

---
*Previous versions: see release tags on GitHub.*
