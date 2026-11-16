using Dalamud.Interface.Windowing;

namespace RotationSolver.UI;

internal abstract class CtrlWindow(string name) : Window(name, BaseFlags)
{
	public const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoScrollbar
						| ImGuiWindowFlags.NoNav
						| ImGuiWindowFlags.NoScrollWithMouse;

	public override void PreDraw()
	{
		Basic.Configuration.Configs config = Service.Config;
		Vector4 bgColor = config.IsControlWindowLock
			? config.ControlWindowLockBg
			: config.ControlWindowUnlockBg;
		ImGui.PushStyleColor(ImGuiCol.WindowBg, bgColor);

		Flags = BaseFlags;
		if (config.IsControlWindowLock)
		{
			Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
		}

		Flags |= ImGuiWindowFlags.NoTitleBar; // Fork: the suite look has no native title bar (a drag strip moves an unlocked window).
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1); // Fork: the suite's 1 px border (was 0).
		ImGui.PushStyleColor(ImGuiCol.Border, LalaUiKit.Theme.SurfaceBorder); // Fork: the suite's border colour.
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, RotationSolver.UI.Pvp.PvpHudChrome.Radius); // Fork: the suite's 8 px radius.

		base.PreDraw();
	}

	public override void PostDraw()
	{
		base.PostDraw();
		ImGui.PopStyleColor(2); // Fork: also the border colour pushed in PreDraw (was 1).
		ImGui.PopStyleVar(2); // Fork: also the window rounding pushed in PreDraw (was 1).
	}

	public sealed override void Draw() // Fork: the shared base draws the drag strip of an unlocked window for both HUDs.
	{
		RotationSolver.UI.Pvp.PvpHudChrome.DragStrip(Service.Config.IsControlWindowLock); // Fork: drag strip, unlocked only.
		DrawContent(); // Fork: the window's own content (the former Draw override).
	}

	protected abstract void DrawContent(); // Fork: replaces the Draw override of the two HUD windows.
}