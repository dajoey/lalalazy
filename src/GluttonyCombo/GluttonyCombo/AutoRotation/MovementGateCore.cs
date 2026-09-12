#region

using System.Collections.Generic;
using System.Numerics;

#endregion

namespace GluttonyCombo.AutoRotation;

/// <summary>
///     PURE safety gate for auto-fired movement abilities (Policy A, t_8d711ea6).
///     One predicate every auto-fired dash/gap-closer passes before its per-job
///     logic may fire: NOT dodging, NOT mid-dash, landing point NOT in a live
///     danger zone. Compiled into <c>tests/GluttonyCombo.SmartMoverHarness</c>;
///     a Dalamud type leaking in breaks that build, which is the point.
/// </summary>
internal static class MovementGateCore
{
    /// <summary>
    ///     The gate verdict. <paramref name="gateEnabled"/> = false must pass
    ///     EVERYTHING through untouched - that is the stock-behavior-identical case.
    /// </summary>
    internal static bool Allowed(bool gateEnabled, bool dodging, bool dashing, bool landingUnsafe)
        => !gateEnabled || (!dodging && !dashing && !landingUnsafe);
}

