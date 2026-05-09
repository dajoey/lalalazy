namespace RotationSolver.Basic.Data;

/// <summary>
/// Represents the command for the next action.
/// </summary>
/// <param name="Act">The action itself.</param>
/// <param name="DeadTime">The time when the action should stop.</param>
public record NextAct(IAction Act, DateTime DeadTime);