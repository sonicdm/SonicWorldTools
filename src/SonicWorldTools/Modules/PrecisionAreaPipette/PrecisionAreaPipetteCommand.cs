using SonicWorldTools.Config;

namespace SonicWorldTools.Modules.PrecisionAreaPipette
{
	/// <summary>Terminal commands matching Infinity Hammer YAML <c>command:</c> (instant tool).</summary>
	internal static class PrecisionAreaPipetteCommand
	{
		internal const string CommandName = "sonic_precision_pipette";

		internal const string PickUpCommandName = "sonic_precision_pipette_pickup";

		private static bool _registered;

		internal static void Register()
		{
			if (_registered)
			{
				return;
			}

			_registered = true;
			new Terminal.ConsoleCommand(
				CommandName,
				"SonicWorldTools: activate Precision Area Pipette",
				_ =>
				{
					if (!PluginConfig.IsPrecisionAreaPipetteEnabled())
					{
						SonicWorldToolsPlugin.ModLogger?.LogInfo(
							"SonicWorldTools / Precision Area Pipette disabled; ignored.");
						return;
					}

					PrecisionAreaPipetteController controller = PrecisionAreaPipetteController.Instance;
					if (controller == null)
					{
						SonicWorldToolsPlugin.ModLogger?.LogWarning(
							"Precision Area Pipette controller unavailable (Infinity Hammer required).");
						return;
					}

					controller.Activate();
				});

			new Terminal.ConsoleCommand(
				PickUpCommandName,
				"SonicWorldTools: hand locked selection to Infinity Hammer (copy ghost)",
				_ =>
				{
					if (!PluginConfig.IsPrecisionAreaPipetteEnabled())
					{
						SonicWorldToolsPlugin.ModLogger?.LogInfo(
							"SonicWorldTools / Precision Area Pipette disabled; ignored.");
						return;
					}

					PrecisionAreaPipetteController controller = PrecisionAreaPipetteController.Instance;
					if (controller == null)
					{
						SonicWorldToolsPlugin.ModLogger?.LogWarning(
							"Precision Area Pipette controller unavailable (Infinity Hammer required).");
						return;
					}

					controller.TryPickUpSelection();
				});
		}
	}
}
