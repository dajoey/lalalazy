using ECommons.Logging;
using RotationSolver.UI;

namespace RotationSolver.Helpers;

public static class DownloadHelper
{
	public static IncompatiblePlugin[] IncompatiblePlugins { get; private set; } = [];

	public static Task DownloadAsync()
	{
		IncompatiblePlugins = [];
		return Task.CompletedTask;
	}
}
