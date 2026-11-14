using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;

namespace RotationSolver.UI;

internal static class ImguiTooltips
{
	private const ImGuiWindowFlags TooltipFlag =
		  ImGuiWindowFlags.Tooltip |
		  ImGuiWindowFlags.NoMove |
		  ImGuiWindowFlags.NoSavedSettings |
		  ImGuiWindowFlags.NoBringToFrontOnFocus |
		  ImGuiWindowFlags.NoDecoration |
		  ImGuiWindowFlags.NoInputs |
		  ImGuiWindowFlags.AlwaysAutoResize;

	private const string TooltipId = "PvPSolver Tooltips";

	/// <summary>
	/// Displays a tooltip when the item is hovered.
	/// </summary>
	/// <param name="text">The text to display in the tooltip.</param>
	public static void HoveredTooltip(string? text)
	{
		if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(text))
		{
			ShowTooltip(() => ImGui.Text(text));
		}
	}

	/// <summary>
	/// Displays a tooltip with the specified text.
	/// </summary>
	/// <param name="text">The text to display in the tooltip.</param>
	public static void ShowTooltip(string? text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			ShowTooltip(() => ImGui.Text(text));
		}
	}

	/// <summary>
	/// Displays a tooltip with the specified action.
	/// </summary>
	/// <param name="act">The action to perform to render the tooltip content.</param>
	public static void ShowTooltip(Action? act)
	{
		if (act == null || Service.Config.ShowTooltips != true)
		{
			return;
		}

		using var tooltip = Layout.TooltipScope.Begin(TooltipId, TooltipFlag,
			new Vector2(150, 0) * ImGuiHelpers.GlobalScale, new Vector2(1200, 1500) * ImGuiHelpers.GlobalScale,
			ImGui.GetIO().MousePos);

		if (tooltip.Opened)
		{
			act();
		}
	}
}