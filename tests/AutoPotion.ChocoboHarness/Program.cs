using AutoPotion;

namespace AutoPotion.ChocoboHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Check("two attackers does not engage", Step(new(), 2, true, ChocoboStance.Free) == ChocoboStance.None);
        Check("exactly three attackers engages Healer", Step(new(), 3, true, ChocoboStance.Free) == ChocoboStance.Healer);
        Check("three attackers engages even at full HP", Step(new(), 3, true, ChocoboStance.Defender) == ChocoboStance.Healer);
        Check("full HP requires max HP to be known", !ChocoboControl.IsFullyHealed(0, 0));
        Check("one HP missing blocks full-health gate", !ChocoboControl.IsFullyHealed(999, 1000));
        Check("exact maximum HP passes full-health gate", ChocoboControl.IsFullyHealed(1000, 1000));
        Check("attacker threshold is exactly three", ChocoboControl.HealerAttackerThreshold == 3);
        Check("attacker requires targetable hostile enemy targeting player",
            ChocoboControl.CountsAsAttacker(true, true, true)
            && !ChocoboControl.CountsAsAttacker(false, true, true)
            && !ChocoboControl.CountsAsAttacker(true, false, true)
            && !ChocoboControl.CountsAsAttacker(true, true, false));

        RestoresOnlyWhenBothGatesClear();
        RestoresEveryPreviousStance();
        ReassertsAndStopsAfterObservedRestore();
        ResetsWithoutValidContext();
        DefersUnknownStanceUntilReadable();
        PreservesStanceAcrossFeatureToggle();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static ChocoboStance Step(ChocoboControl c, int attackers, bool fullHp, ChocoboStance stance,
        bool enabled = true, bool companion = true, bool living = true) =>
        c.Update(enabled, companion, living, attackers, fullHp, stance);

    private static void RestoresOnlyWhenBothGatesClear()
    {
        var c = new ChocoboControl();
        Check("Free encounter enters Healer", Step(c, 3, false, ChocoboStance.Free) == ChocoboStance.Healer);
        Check("four attackers remains Healer", Step(c, 4, true, ChocoboStance.Healer) == ChocoboStance.None);
        Check("two attackers but injured remains Healer", Step(c, 2, false, ChocoboStance.Healer) == ChocoboStance.None);
        Check("zero attackers but injured remains Healer", Step(c, 0, false, ChocoboStance.Healer) == ChocoboStance.None);
        Check("full HP but three attackers remains Healer", Step(c, 3, true, ChocoboStance.Healer) == ChocoboStance.None);
        Check("two attackers AND full HP restores Free", Step(c, 2, true, ChocoboStance.Healer) == ChocoboStance.Free);
        Check("observed Free completes encounter", Step(c, 2, true, ChocoboStance.Free) == ChocoboStance.None && !c.IsEngaged);
    }

    private static void RestoresEveryPreviousStance()
    {
        foreach (var original in new[] { ChocoboStance.Free, ChocoboStance.Defender, ChocoboStance.Attacker })
        {
            var c = new ChocoboControl();
            Step(c, 3, false, original);
            Check($"{original} is restored", Step(c, 0, true, ChocoboStance.Healer) == original);
        }

        var existingHealer = new ChocoboControl();
        Check("already Healer needs no entry command", Step(existingHealer, 3, false, ChocoboStance.Healer) == ChocoboStance.None);
        Check("already Healer remains Healer and completes", Step(existingHealer, 0, true, ChocoboStance.Healer) == ChocoboStance.None && !existingHealer.IsEngaged);
    }

    private static void ReassertsAndStopsAfterObservedRestore()
    {
        var c = new ChocoboControl();
        Step(c, 3, false, ChocoboStance.Free);
        Check("manual non-Healer stance is corrected while engaged", Step(c, 3, false, ChocoboStance.Attacker) == ChocoboStance.Healer);
        Check("restore is repeated until game state observes it", Step(c, 0, true, ChocoboStance.Healer) == ChocoboStance.Free
            && Step(c, 0, true, ChocoboStance.Healer) == ChocoboStance.Free);
        Check("observed restore emits no further command", Step(c, 0, true, ChocoboStance.Free) == ChocoboStance.None);
    }

    private static void ResetsWithoutValidContext()
    {
        var control = new ChocoboControl();
        control.Update(true, true, true, 3, true, ChocoboStance.Free);
        control.Update(true, false, true, 0, true, ChocoboStance.Healer);
        Check("MissingCompanionClearsEncounter", !control.IsEngaged && control.PreviousStance == ChocoboStance.None);

        control = new ChocoboControl();
        control.Update(true, true, true, 3, true, ChocoboStance.Free);
        control.Update(true, true, false, 0, true, ChocoboStance.Healer);
        Check("DeadPlayerClearsEncounter", !control.IsEngaged && control.PreviousStance == ChocoboStance.None);
    }

    private static void DefersUnknownStanceUntilReadable()
    {
        foreach (var unknown in new[] { ChocoboStance.None, unchecked((ChocoboStance)999) })
        {
            var control = new ChocoboControl();
            Check($"UnknownStance{unknown}Defers", control.Update(true, true, true, 3, true, unknown)
                == ChocoboStance.None && !control.IsEngaged && control.PreviousStance == ChocoboStance.None);
            Check($"UnknownStance{unknown}RecoversDefender", control.Update(true, true, true, 3, true, ChocoboStance.Defender)
                == ChocoboStance.Healer && control.PreviousStance == ChocoboStance.Defender);
            Check($"UnknownStance{unknown}RestoresDefender", control.Update(true, true, true, 0, true, ChocoboStance.Healer)
                == ChocoboStance.Defender && control.PreviousStance == ChocoboStance.Defender);
        }
    }

    private static void PreservesStanceAcrossFeatureToggle()
    {
        var control = new ChocoboControl();
        control.Update(true, true, true, 3, true, ChocoboStance.Defender);
        Check("FeatureDisableSuspendsCommands", control.Update(false, true, true, 0, true, ChocoboStance.Healer)
            == ChocoboStance.None && control.PreviousStance == ChocoboStance.Defender);
        Check("FeatureReenableRestoresDefender", control.Update(true, true, true, 0, true, ChocoboStance.Healer)
            == ChocoboStance.Defender && control.PreviousStance == ChocoboStance.Defender);
    }

    private static void Check(string name, bool ok)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS  {name}"); }
        else { _fail++; Console.WriteLine($"FAIL  {name}"); }
    }
}
