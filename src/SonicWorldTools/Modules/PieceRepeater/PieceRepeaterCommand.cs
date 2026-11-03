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
				"Activate SonicWorldTools Piece Repeater. Select a build piece first, or middle-click a placed piece to sample it.",
				_ => PieceRepeaterController.CaptureAndActivate());
		}
	}
}
