// 0.1.7.0 (card t_5191608a): the sequential resume-mode stage machine. Pure Core - no game, no UI
// framework references; the plugin layer translates a NeedsUser stage into the one Resume modal.
namespace LazyCrafter.Core;

/// <summary>
/// The cart run's sequential stages (0.1.7.0, card t_5191608a). A staged run runs the
/// user-intervention-requiring parts first - the shopping stops the player must make in person -
/// and once the controller believes the rest of the cart needs no human, the run is
/// <see cref="Unattended"/> and behaves exactly as it did before the stage machine existed.
/// </summary>
public enum RunStage
{
    /// <summary>The shopping stops the player must make: gil vendors, the market board, currency shops, manual sources.</summary>
    ShoppingTrip,
    /// <summary>Materials the automation handles itself: retainer ventures (ARC) and gathering (GBR). No human step.</summary>
    GatherPlan,
    /// <summary>The queued crafts for this wave (Artisan). No human step.</summary>
    CraftQueue,
    /// <summary>The tail of the run: everything left is believed to need no user intervention.</summary>
    Unattended,
}

/// <summary>How one stage ended.</summary>
public enum StageOutcome
{
    /// <summary>The stage finished and needs no human: the machine auto-continues to the next stage.</summary>
    Done,
    /// <summary>The stage needs the player before the run may continue. Exactly one popup emission per transition.</summary>
    NeedsUser,
    /// <summary>The stage failed with a reason; the reason is recorded once and the run stops.</summary>
    Failed,
}

/// <summary>
/// The boundary coordinator for a staged run (0.1.7.0, card t_5191608a). The controller holds the
/// ordered stage list and records each stage's outcome; the plugin layer asks it two things:
/// <see cref="Popup"/> (is a NEW popup emission due?) for the one-shot notification, and
/// <see cref="PopupShowing"/> (is a stage blocked right now?) for the modal that stays on screen.
/// <para>
/// The popup contract - the whole point of the feature - is dedupe: a blocked stage emits its
/// popup ONCE, at the stage transition, and never re-emits it on a later frame tick. That holds
/// even if the dispatcher reports the same blocked stage every tick, because
/// <see cref="Record"/> only re-arms the emission for a genuinely NEW message.
/// </para>
/// <para>
/// Stage results are recorded by the dispatcher at real boundaries (a wave blocked on shopping,
/// a gather plan handed off, a craft queue started); the controller never inspects game state
/// itself, so it stays pure and the harness drives it offline.
/// </para>
/// </summary>
public sealed class RunStageController
{
    private readonly RunStage[] _order;
    private int _index;
    private StageOutcome _outcome = StageOutcome.Done;
    private string _message = "";
    private bool _popupConsumed = true;   // nothing emitted yet; armed only by a NeedsUser transition

    /// <summary>The stage the run is currently in. An empty order reads as <see cref="RunStage.Unattended"/>.</summary>
    public RunStage Current => _order.Length == 0 ? RunStage.Unattended : _order[_index];

    /// <summary>
    /// True when the run believes the rest needs no player - Joey's "unattended" mode. The only
    /// stage that can need the user is <see cref="RunStage.ShoppingTrip"/>; GatherPlan and
    /// CraftQueue are the automation's own stages. So the run is unattended once it has moved
    /// past the shopping stage with nothing blocked: a cart with no shopping work is unattended
    /// from its first wave, and a staged run goes unattended the moment the last shopping stop
    /// is bought - even while gathers and crafts are still running. An empty order (the off
    /// switch) is unattended from the first tick.
    /// </summary>
    public bool IsUnattended =>
        _order.Length == 0
        || (_outcome == StageOutcome.Done && _index > Array.IndexOf(_order, RunStage.ShoppingTrip));

    /// <summary>
    /// The one-shot popup emission that is due right now, or null. A NeedsUser transition arms it
    /// exactly once; <see cref="PopupSurfaced"/> (or <see cref="Advance"/>) consumes it, and
    /// re-recording the SAME blocked message never re-arms it.
    /// </summary>
    public string? Popup => _outcome == StageOutcome.NeedsUser && !_popupConsumed ? _message : null;

    /// <summary>The blocked-stage message currently on screen (the modal's text), or null when no stage is blocked.</summary>
    public string? PopupShowing => _outcome == StageOutcome.NeedsUser ? _message : null;

    /// <summary>The recorded failure reason of the run, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>
    /// A controller with no stages: the whole run is unattended from the first tick. This is the
    /// off switch - <c>SequentialInterventionMode</c> off creates no controller at all, and an
    /// empty order behaves identically if one is created anyway.
    /// </summary>
    public RunStageController(params RunStage[] order) : this(order as IReadOnlyList<RunStage>) { }

    /// <summary>See the parameterless overload; the list form is what the dispatcher passes.</summary>
    public RunStageController(IReadOnlyList<RunStage> order) => _order = order.ToArray();

    /// <summary>
    /// Record a stage's outcome.
    /// <para>
    /// <see cref="StageOutcome.Done"/> auto-continues: the stage needed no human, so the machine
    /// moves to the next stage (index capped at the last). <see cref="StageOutcome.NeedsUser"/>
    /// parks the machine and arms exactly ONE popup emission - unless the SAME message is already
    /// the blocked state, in which case nothing re-arms (the per-tick re-record cannot spam).
    /// A <see cref="StageOutcome.Failed"/> reason is recorded once.
    /// </para>
    /// <para>
    /// Only the current stage - or the one just advanced from, so a Resume that changed nothing
    /// can honestly re-block - is accepted; a record for a stage further back is a late echo and
    /// is ignored. A <see cref="StageOutcome.Failed"/> is the one exception: it always lands,
    /// whatever stage the machine sits on, because the run is over the moment it does.
    /// </para>
    /// </summary>
    public void Record(RunStage stage, StageOutcome outcome, string message = "")
    {
        if (_order.Length == 0) return;
        var at = Array.IndexOf(_order, stage);
        if (at < 0) return;                                   // not this run's stage: ignored
        if (outcome == StageOutcome.Failed)
        {
            // A failure is terminal and always lands, whatever stage the machine sits on: the
            // dispatcher can report a hand-off that failed before the stage "started" - the
            // position guard below exists for late echoes of stages already passed, not for
            // failures. The reason is recorded once; the same reason re-reported every tick
            // never duplicates it.
            _outcome = StageOutcome.Failed;
            if (string.IsNullOrEmpty(Failure) || !Failure.Equals(message, StringComparison.Ordinal))
                Failure = message ?? "";
            _message = message ?? "";
            _popupConsumed = true;                            // a failed stage never popups
            return;
        }
        if (at != _index && at != _index - 1) return;         // a late echo from a passed stage: ignored
        var sameBlock = outcome == StageOutcome.NeedsUser && _outcome == StageOutcome.NeedsUser
                        && string.Equals(_message, message, StringComparison.Ordinal);
        _outcome = outcome;
        _message = message ?? "";
        if (outcome == StageOutcome.Done)
        {
            if (_index < _order.Length - 1) _index++;   // auto-continue: no human needed
            _popupConsumed = true;
        }
        else if (!sameBlock)
        {
            _popupConsumed = false;                     // a NEW blocked state: exactly one emission
        }
    }

    /// <summary>
    /// The player pressed Resume. Moves on from the blocked stage (the run continues from
    /// recorded state - the dispatcher re-plans from the live bags, the same continuation every
    /// Resume already uses, never a plan restart) and consumes the popup emission. Returns the
    /// stage to continue in.
    /// </summary>
    public RunStage Advance()
    {
        if (_outcome == StageOutcome.NeedsUser)
        {
            if (_index < _order.Length - 1) _index++;
            _outcome = StageOutcome.Done;
            _message = "";
        }
        _popupConsumed = true;
        return Current;
    }

    /// <summary>
    /// The UI layer accepted the popup emission (the notification fired / the modal opened).
    /// Stops the one-shot <see cref="Popup"/> from repeating; <see cref="PopupShowing"/> keeps
    /// describing the blocked state until Resume.
    /// </summary>
    public void PopupSurfaced()
    {
        _popupConsumed = true;
    }
}
