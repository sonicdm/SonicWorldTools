using HarmonyLib;
using UnityEngine;
using SonicWorldTools.Config;
using SonicWorldTools.Modules.PieceRepeater;
using SonicWorldTools.Modules.ScatterProps;

namespace SonicWorldTools.Core
{
	/// <summary>Consumes vanilla dismantle while any selected SonicWorldTools tool owns MMB.</summary>
	[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
	internal static class OwnedToolRemoveButtonInputPatch
	{
		private static bool Prefix(string name, ref bool __result)
		{
			if (!string.Equals(name, "Remove", System.StringComparison.OrdinalIgnoreCase)
			    || !Player.m_localPlayer
			    || !ToolRuntime.InBuildMode
			    || !ToolRuntime.InfinityHammerEquipped
			    || ToolRuntime.TextInputBlocked
			    || !ToolRuntime.TryGetSelectedToolName(out string selectedTool)
			    || !OwnsMiddleMouse(selectedTool)
			    || !ToolInput.WasMouseButtonDown(2))
				return true;

			__result = false;
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[ToolInput] Consumed vanilla Remove edge for active MMB tool; frame="
				+ Time.frameCount + " IHtool='" + selectedTool + "'.");
			return false;
		}

		internal static bool OwnsMiddleMouse(string selectedTool)
		{
			if (ToolInstaller.IsPieceRepeaterToolName(selectedTool))
				return PluginConfig.PieceRepeaterSampleKey.Value == KeyCode.Mouse2;
			if (string.Equals(selectedTool, "Scatter Props", System.StringComparison.OrdinalIgnoreCase))
				return PluginConfig.ScatterSampleKey.Value == KeyCode.Mouse2;
			if (string.Equals(selectedTool, ToolInstaller.ToolName, System.StringComparison.OrdinalIgnoreCase))
				return PluginConfig.IsPrecisionAreaPipetteEnabled()
				       && (PluginConfig.ExcludePieceKey.Value == KeyCode.Mouse2
				           || PluginConfig.PickUpKey.Value == KeyCode.Mouse2);
			return false;
		}

		internal static bool ShouldBlockHoveredPieceDestruction(WearNTear wear, out string selectedTool)
		{
			selectedTool = null;
			Player player = Player.m_localPlayer;
			if (!wear || !player || !ToolRuntime.InBuildMode || !ToolRuntime.InfinityHammerEquipped
			    || ToolRuntime.TextInputBlocked
			    || !ToolRuntime.TryGetSelectedToolName(out selectedTool)
			    || !ToolInstaller.IsPieceRepeaterToolName(selectedTool)
			    || PluginConfig.PieceRepeaterSampleKey.Value != KeyCode.Mouse2
			    || (!ToolInput.WasMouseButtonDown(2) && !ToolInput.WasMouseButtonHeld(2)
			        && !ToolInput.WasMouseButtonPressedRecently(2, 30)))
				return false;

			Piece target = wear.GetComponent<Piece>();
			return target && player.GetHoveringPiece() == target;
		}
	}

	/// <summary>Routes vanilla player actions through the active tool's input policy.</summary>
	[HarmonyPatch(typeof(Player), "TryPlacePiece")]
	internal static class TryPlacePiecePatch
	{
		private static bool Prefix(Player __instance, ref bool __result)
		{
			if (!ToolActionGate.ShouldSuppress(ToolPlayerAction.BuildPlacement, __instance)) return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(Player), "PlacePiece")]
	internal static class PlacePiecePatch
	{
		private static bool Prefix(Player __instance)
		{
			if (ToolActionGate.IsTrustedPlacement) return true;
			return !ToolActionGate.ShouldSuppress(ToolPlayerAction.BuildPlacement, __instance);
		}
	}

	[HarmonyPatch(typeof(Player), "PlayerAttackInput")]
	internal static class PlayerAttackInputPatch
	{
		private static bool Prefix(Player __instance) =>
			!ToolActionGate.ShouldSuppress(ToolPlayerAction.Attack, __instance);
	}

	[HarmonyPatch(typeof(Player), "RemovePiece")]
	[HarmonyPriority(Priority.First)]
	internal static class PlayerRemovePiecePatch
	{
		private static int _lastTraceFrame = -1;

		private static bool Prefix(Player __instance)
		{
			bool suppress = ToolActionGate.ShouldSuppress(ToolPlayerAction.Remove, __instance);
			if (__instance == Player.m_localPlayer
			    && _lastTraceFrame != Time.frameCount
			    && (ToolInput.WasMouseButtonDown(2) || ToolActionGate.ShouldSuppress(ToolPlayerAction.Remove, __instance)))
			{
				_lastTraceFrame = Time.frameCount;
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[InputTrace] RemovePiece hook frame=" + UnityEngine.Time.frameCount
					+ " suppressed=" + suppress
					+ " MMBdown=" + ToolInput.WasMouseButtonDown(2)
					+ " MMBheld=" + ToolInput.WasMouseButtonHeld(2)
					+ " lineActive=" + PieceRepeaterController.IsActive
					+ " ihTool='" + (ToolRuntime.TryGetSelectedToolName(out string selectedTool) ? selectedTool : "<none>") + "'.");
			}
			return !suppress;
		}
	}

	[HarmonyPatch(typeof(Player), "Interact")]
	internal static class PlayerInteractPatch
	{
		private static bool Prefix(Player __instance) =>
			!ToolActionGate.ShouldSuppress(ToolPlayerAction.Interact, __instance);
	}

	[HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
	internal static class MenuShowEscapePatch
	{
		private static bool Prefix() =>
			!ToolActionGate.ShouldSuppress(ToolPlayerAction.PauseMenu, Player.m_localPlayer);
	}
}
