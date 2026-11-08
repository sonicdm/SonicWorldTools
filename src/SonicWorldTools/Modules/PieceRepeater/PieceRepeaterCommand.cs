namespace SonicWorldTools.Modules.PieceRepeater
{
	internal static class PieceRepeaterCommand
	{
		internal const string CommandName = "sonic_piece_repeater";
		private static bool _registered;

		internal static void Register()
		{
			if (_registered) return;
			_registered = true;
			new Terminal.ConsoleCommand("sonic_hint_status", "Read current key-hint state", args =>
			{
				var manager = Jotunn.Managers.KeyHintManager.Instance;
				foreach (string name in new[] { "HasInitBaseGameObjects", "KeyHintInstance", "KeyHintContainer", "KeyHints", "KeyHintObjects" })
				{
					object value = HarmonyLib.AccessTools.Field(manager.GetType(), name)?.GetValue(manager);
					args.Context.AddString(name + "=" + value);
					if (value is System.Collections.IDictionary dictionary)
						foreach (System.Collections.DictionaryEntry pair in dictionary)
							{
								args.Context.AddString("  " + pair.Key + "=" + pair.Value);
								if (pair.Value is UnityEngine.GameObject go)
								{
									args.Context.AddString("  active=" + go.activeInHierarchy);
									foreach (var label in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
										args.Context.AddString("  label=" + label.text);
								}
							}
				}
			});
			new Terminal.ConsoleCommand(
				CommandName,
				"Activate Line Tool. Select a build piece first, or use the configured sample key on a placed piece.",
				args => args.Context.TryRunCommand("tool " + SonicWorldTools.Core.ToolInstaller.PieceRepeaterToolName));
			new Terminal.ConsoleCommand(
				"sonic_line_anarchy",
				"Toggle Line Tool Anarchy Mode, or set it with on/off. Samples and network-spawns registered prefabs such as trees and creatures.",
				args =>
				{
					if (args.Length > 1 && bool.TryParse(args[1], out bool enabled))
						SonicWorldTools.Config.PluginConfig.PieceRepeaterAnarchyMode.Value = enabled;
					else if (args.Length > 1 && (args[1].Equals("on", System.StringComparison.OrdinalIgnoreCase)
					                         || args[1].Equals("off", System.StringComparison.OrdinalIgnoreCase)))
						SonicWorldTools.Config.PluginConfig.PieceRepeaterAnarchyMode.Value = args[1].Equals("on", System.StringComparison.OrdinalIgnoreCase);
					else if (args.Length == 1)
						SonicWorldTools.Config.PluginConfig.PieceRepeaterAnarchyMode.Value = !SonicWorldTools.Config.PluginConfig.PieceRepeaterAnarchyMode.Value;
					else
					{
						args.Context.AddString("Usage: sonic_line_anarchy [on|off]");
						return;
					}
					PieceRepeaterController.Instance?.RefreshSettingsPreview();
					args.Context.AddString("Line Tool Anarchy Mode "
					                       + (SonicWorldTools.Config.PluginConfig.PieceRepeaterAnarchyMode.Value ? "ON" : "OFF")
					                       + ". Fallback spacing is "
					                       + SonicWorldTools.Config.PluginConfig.PieceRepeaterGroundPropSpacing.Value.ToString("0.##") + " m.");
				});
			new Terminal.ConsoleCommand(
				SonicWorldTools.Core.ToolInstaller.ShapeToolCommand,
				"Activate Shape Tool. Select a build piece first, or use the configured sample key on a placed piece.",
				args => args.Context.TryRunCommand("tool " + SonicWorldTools.Core.ToolInstaller.ShapeToolName));
		}
	}
}
