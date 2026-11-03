using HarmonyLib;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using SonicWorldTools.Modules.PrecisionAreaPipette;
using SonicWorldTools.Modules.PieceRepeater;
using UnityEngine;

namespace SonicWorldTools.Integration
{
	/// <summary>
	/// Arm Precision Area Pipette on menu commit (not hover/navigated) and when IH
	/// exposes our ToolSelection. Intentionally smaller than Dismantleheim's tracker —
	/// no piece-table re-equip poll (that fights other Instant tools left in the slot).
	/// Bring-up builds log the activation path generously under <c>[ToolId]</c>.
	/// </summary>
	internal static class ToolIdentityTracker
	{
		private static string _lastLoggedToolSelection = string.Empty;

		internal static bool IsOurToolLabel(string label)
		{
			if (string.IsNullOrEmpty(label))
			{
				return false;
			}

			// This identity belongs only to Precision Select. Piece Repeater has its
			// own controller and must never arm the pipette when its menu entry commits.
			return label.IndexOf(ToolInstaller.ToolName, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		internal static bool IsOurPiece(Piece piece)
		{
			if ((Object)(object)piece == (Object)null)
			{
				return false;
			}

			string name = piece.m_name ?? string.Empty;
			string go = piece.gameObject != null ? piece.gameObject.name ?? string.Empty : string.Empty;
			return IsOurToolLabel(name) || IsOurToolLabel(go);
		}

		internal static string DescribePiece(Piece piece)
		{
			if ((Object)(object)piece == (Object)null)
			{
				return "<null>";
			}

			string name = piece.m_name ?? string.Empty;
			string go = piece.gameObject != null ? piece.gameObject.name ?? string.Empty : string.Empty;
			return "name='" + name + "' go='" + go + "'";
		}

		/// <summary>
		/// ScriptEngine-safe: when IH Instant menu ran <c>tool OurName</c>, a ToolSelection
		/// ghost is active — arm without waiting for Attack.
		/// </summary>
		internal static void Tick(PrecisionAreaPipetteController controller)
		{
			if (controller == null || !PluginConfig.IsPrecisionAreaPipetteEnabled())
			{
				return;
			}

			Player player = Player.m_localPlayer;
			if ((Object)(object)player == (Object)null || !player.InPlaceMode())
			{
				return;
			}

			bool hasTool = InfinityHammerBridge.TryGetActiveToolSelectionName(out string toolName);
			string selKey = hasTool ? toolName : "<none>";
			if (!string.Equals(selKey, _lastLoggedToolSelection, System.StringComparison.Ordinal))
			{
				_lastLoggedToolSelection = selKey;
				Log(
					"ToolSelection poll: "
					+ (hasTool ? ("'" + toolName + "' our=" + IsOurToolLabel(toolName)) : "none")
					+ " pipetteActive=" + controller.IsActive
					+ " place=" + player.InPlaceMode());
			}

			// Do not Selection.Clear / SelectEmpty while active — that NREs UpdatePlacementGhost
			// (same failure as early Activate clears). Sonic's live aim marker coexists with IH.
			if (controller.IsActive)
			{
				if (hasTool && !IsOurToolLabel(toolName))
				{
					controller.Deactivate("other-ih-tool-selection");
				}

				return;
			}

			if (!hasTool || !IsOurToolLabel(toolName))
			{
				return;
			}

			Log("Arming from ToolSelection '" + toolName + "'.");
			controller.Activate("ih-tool-selection");
		}

		internal static void HandleBuildUiPiece(Piece piece, string via)
		{
			bool match = IsOurPiece(piece);
			bool commit = via == "buildui-on-select-piece" || via == "buildui-piece-click";

			Log(
				"BuildUi " + via
				+ " commit=" + commit
				+ " match=" + match
				+ " " + DescribePiece(piece));

			if (!commit)
			{
				return;
			}

			PrecisionAreaPipetteController controller = PrecisionAreaPipetteController.Instance;
			if (controller == null || !PluginConfig.IsPrecisionAreaPipetteEnabled())
			{
				Log(
					"BuildUi commit ignored — controllerNull="
					+ (controller == null)
					+ " pipetteEnabled="
					+ PluginConfig.IsPrecisionAreaPipetteEnabled());
				return;
			}

			if (match)
			{
				Log("BuildUi commit matched our tool — Activate(" + via + ").");
				controller.Activate(via);
				return;
			}

			if (!controller.IsActive || piece == null || string.IsNullOrEmpty(piece.m_name))
			{
				return;
			}

			Log("BuildUi other tool while active — Deactivate. " + DescribePiece(piece));
			controller.Deactivate("other-tool-selected");
		}

		internal static void Log(string message)
		{
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[ToolId] " + message);
		}
	}

	/// <summary>
	/// DeepNorth SelectTool runs Instant commands here and returns false —
	/// Player.SetSelectedPiece never runs. Activate on commit before IH.
	/// </summary>
	[HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OnSelectPiece))]
	[HarmonyPriority(Priority.First)]
	internal static class BuildUiOnSelectPiecePatch
	{
		private static void Prefix(Piece __0)
		{
			ToolIdentityTracker.HandleBuildUiPiece(__0, "buildui-on-select-piece");
			PieceRepeaterController.OnOtherBuildPieceSelected(__0);
		}
	}

	[HarmonyPatch(typeof(BuildUiPieceButton), "OnClickButton")]
	[HarmonyPriority(Priority.First)]
	internal static class BuildUiPieceButtonClickPatch
	{
		private static void Prefix(BuildUiPieceButton __instance)
		{
			Piece piece = __instance != null ? __instance.Piece : null;
			ToolIdentityTracker.HandleBuildUiPiece(piece, "buildui-piece-click");
			PieceRepeaterController.OnOtherBuildPieceSelected(piece);
		}
	}

	[HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand), typeof(string), typeof(bool), typeof(bool))]
	internal static class TerminalIhToolCommandPatch
	{
		private static void Prefix(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			if (text.StartsWith("tool ", System.StringComparison.OrdinalIgnoreCase)
			    && ToolInstaller.IsPieceRepeaterToolName(text.Substring(5).Trim()))
			{
				PieceRepeaterController.CaptureAndActivate();
				return;
			}

			if (text.StartsWith(PieceRepeaterCommand.CommandName, System.StringComparison.OrdinalIgnoreCase))
			{
				PieceRepeaterController.CaptureAndActivate();
				return;
			}

			if (text.StartsWith("tool ", System.StringComparison.OrdinalIgnoreCase)
			    && PieceRepeaterController.IsActive
			    && !ToolInstaller.IsPieceRepeaterToolName(text.Substring(5).Trim()))
			{
				PieceRepeaterController.Instance?.Deactivate("other-ih-tool-command");
			}
			else if (PieceRepeaterController.IsActive
			         && text.StartsWith(PrecisionAreaPipetteCommand.CommandName, System.StringComparison.OrdinalIgnoreCase))
			{
				PieceRepeaterController.Instance?.Deactivate("precision-tool-command");
			}

			if (!PluginConfig.IsPrecisionAreaPipetteEnabled()) return;

			PrecisionAreaPipetteController controller = PrecisionAreaPipetteController.Instance;

			if (text.StartsWith("tool ", System.StringComparison.OrdinalIgnoreCase))
			{
				string toolName = text.Substring(5).Trim();
				bool ours = ToolIdentityTracker.IsOurToolLabel(toolName);
				ToolIdentityTracker.Log(
					"Terminal '" + text + "' ours=" + ours
					+ " pipetteActive=" + (controller != null && controller.IsActive));
				if (ours && controller != null && !controller.IsActive)
				{
					controller.Activate("ih-tool-command");
				}
				else if (!ours && controller != null && controller.IsActive)
				{
					controller.Deactivate("other-ih-tool-command");
				}

				return;
			}

			if (text.StartsWith(
				    PrecisionAreaPipetteCommand.CommandName,
				    System.StringComparison.OrdinalIgnoreCase))
			{
				ToolIdentityTracker.Log(
					"Terminal Instant '" + text + "' pipetteActive="
					+ (controller != null && controller.IsActive));
				if (controller != null && !controller.IsActive)
				{
					controller.Activate("ih-instant-command");
				}
			}
		}
	}
}
