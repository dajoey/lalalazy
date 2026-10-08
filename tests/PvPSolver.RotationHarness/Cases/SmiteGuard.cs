using System.Reflection;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     Change 1, SMITE (SHARED-1 / NIN-1). T1 by reflection: invoke the real <c>ModifySmitePvP</c> from
///     PvPSolver.Basic on a fresh <c>ActionSetting</c> and assert <c>IgnoreGuard</c>. The game text says Smite
///     ignores Guard when dealing damage; without the flag the target filter drops a guarded enemy.
/// </summary>
internal static class SmiteGuard
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        Console.WriteLine("-- change 1 SMITE (T1 by reflection) --");

        var rotation = BasicAssembly.Type("RotationSolver.Basic.Rotations.CustomRotation");
        var settingType = BasicAssembly.Type("RotationSolver.Basic.Actions.ActionSetting");
        var ignoreGuard = settingType.GetProperty("IgnoreGuard", Any) ?? throw new MissingMemberException("ActionSetting.IgnoreGuard");

        bool Invoke(string method)
        {
            var m = rotation.GetMethod(method, Any) ?? throw new MissingMethodException(rotation.FullName, method);
            var setting = Activator.CreateInstance(settingType)!;
            var args = new[] { setting };
            m.Invoke(null, args);
            return (bool)ignoreGuard.GetValue(setting)!;
        }

        var fresh = (bool)ignoreGuard.GetValue(Activator.CreateInstance(settingType)!)!;
        Harness.Case("a fresh ActionSetting does not ignore Guard", !fresh);

        var smite = Invoke("ModifySmitePvP");
        Harness.Case("real ModifySmitePvP sets IgnoreGuard on the setting", smite, "IgnoreGuard=" + smite);

        // Role actions that the game text says ignore Guard already set the flag; ones that do not must not.
        Harness.Case("real ModifyFullSwingPvP (already ignores Guard) sets IgnoreGuard", Invoke("ModifyFullSwingPvP"));
        Harness.Case("real ModifySprintPvP does not set IgnoreGuard (the flag is not set blanket)", !Invoke("ModifySprintPvP"));

        // Canary: a method that never sets the flag must not be accepted as one that does.
        Harness.Canary("ModifySprintPvP is accepted as ignoring Guard", Invoke("ModifySprintPvP"));
    }
}
