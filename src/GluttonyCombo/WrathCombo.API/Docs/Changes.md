## `0.5.4` <sup>(2026-02-21)</sup>

Added `Enum.AutoRotationConfigOption.IgnoreRangeInBoss`, to get around
(non-IPC settings) `MaxDistance`.\
(available in WrathCombo `1.0.3.17` and later)

## `0.5.1` <sup>(2026-01-19)</sup>

Added an extension to `Enum.CancellationReason` to get the Description.

## `0.5.0` <sup>(2026-01-19)</sup>

All `API.Error`s now inherit from `API.Error.Exception` instead of directly from
`System.Exception`.

## `0.4.4` <sup>(2026-01-17)</sup>

Added `Enum.AutoRotationConfigOption.BypassQuest` and
`BypassFATE` options, to get around `InCombatOnly`.\
(available in WrathCombo `1.0.3.9` and later)

## `0.4.3` <sup>(2025-12-30)</sup>

Added `Enum.AutoRotationConfigOption.DPSAlwaysHardTarget` and
`HealerAlwaysHardTarget` options now that Auto Rotation does not change the hard
target.\
(available in WrathCombo `1.0.3.4` and later)

## `0.4.2` <sup>(2025-12-24)</sup>

Made `Init()` overload with only the `suppressedErrorTypes` parameter have no
default value, to avoid confusion between the overloads.

## `0.4.1` <sup>(2025-12-24)</sup>

Added correct fallback values when error types are suppressed,
primarily for `IsCurrentJobConfiguredOn`, `IsCurrentJobAutoModeOn`, and `SetResult`
methods.

## `0.4.0` <sup>(2025-12-24)</sup>

Added `suppressedErrorTypes` parameter to `.Init()`, allowing any combination of
Exceptions not be re-thrown when caught.\
Added `.Init()` overload that only has `suppressedErrorTypes` and no
`pluginInterface`.

## `0.3.0` <sup>(2025-12-23)</sup>

Added `IDalamudPluginInterface` loading from ECommons, making
`WrathIPCWrapper.InitWrapper()` no longer required (when using ECommons).\
Renamed `WrathIPCWrapper.InitWrapper()` to just `.Init()`.

## `0.2.1` <sup>(2025-12-23)</sup>

Fixed namespace of `ActionRequest` methods.

## `0.2.0` <sup>(2025-12-22)</sup>

There are now wrapper methods for all Raw IPC Methods.\
Marked `Settings` `WrathCombo.API.WrathIPCWrapper.RawMethods` as
obsolete,
to be made internal in `1.1.0` (Wrapper methods are in place for these).

## `0.1.7` <sup>(2025-12-22)</sup>

Marked `AutoRotationConfiguration` `WrathCombo.API.WrathIPCWrapper.RawMethods` as
obsolete,
to be made internal in `1.1.0` (Wrapper methods are in place for these).

## `0.1.6` <sup>(2025-12-22)</sup>

Marked `ActionRequest` `WrathCombo.API.WrathIPCWrapper.RawMethods` as obsolete,
to be made internal in `1.1.0` (Wrapper methods are in place for these).

## `0.1.2` <sup>(2025-12-20)</sup>

Moved `WrathCombo.API.IPC.RawMethods` to
`WrathCombo.API.WrathIPCWrapper.RawMethods`.\
Fixed publishing script to include downloading Dalamud.

## `0.1.1` <sup>(2025-12-20)</sup>

Marked `Utility` `WrathCombo.API.IPC.RawMethods` as obsolete,
to be made internal in `1.1.0` (Wrapper methods are in place for these).

## `0.1.0` <sup>(2025-12-20)</sup>

Added Raw IPC Subscriber Methods for all IPC endpoints.\
Stubbed Wrapper methods for IPC Methods.\
Added Exceptions to categorize all encounterable Exceptions.\
Designed, but didn't fully implement, wrapper method documentation.

## `0.0.9` <sup>(2025-12-19)</sup>

Finalize the initial Enums and layout for the package.\
Finished publishing script work, and moved the package to be under puni.sh.\
Fully adopt this package in place of the same data that was in WrathCombo
(in [WrathCombo#948](https://github.com/PunishXIV/WrathCombo/pull/948)).

## `0.0.1` <sup>(2025-12-19)</sup>

Initial Release.\
Includes:

- All Enums from WrathCombo
  (from `WrathCombo.Services.IPC` and `WrathCombo.AutoRotation`)
- A new extension to give the value type for `AutoRotationConfigOption` values.