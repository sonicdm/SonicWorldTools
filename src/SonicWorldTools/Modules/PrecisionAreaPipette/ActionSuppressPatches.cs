using HarmonyLib;
using SonicWorldTools.Modules.ScatterProps;
using SonicWorldTools.Modules.PieceRepeater;
using UnityEngine;

namespace SonicWorldTools.Modules.PrecisionAreaPipette
{
	/// <summary>
	/// While Precision Area Pipette owns LMB for corners / refine, block Hammer placement
	/// so Attack does not spawn pieces.
	/// </summary>
	internal static class PipetteOwnership
	{
		internal static bool BlocksVanillaPlacement()
		{
			PrecisionAreaPipetteController ctrl = PrecisionAreaPipetteController.Instance;
			return (ctrl != null && ctrl.IsActive) || PieceRepeaterController.OwnsPlayerInput;
		}
	}

	[HarmonyPatch(typeof(Player), "Update")]
	[HarmonyPriority(Priority.First)]
	internal static class PieceRepeaterSettingsHotkeyPatch
	{
		private static void Prefix(Player __instance)
		{
			if ((Object)(object)__instance == (Object)(object)Player.m_localPlayer)
				PieceRepeaterController.TryConsumeSettingsHotkey();
		}
	}

	[HarmonyPatch(typeof(Player), "TryPlacePiece")]
	internal static class TryPlacePiecePatch
	{
		private static bool Prefix(Player __instance, ref bool __result)
		{
			if (!PipetteOwnership.BlocksVanillaPlacement())
			{
				return true;
			}

			if ((Object)(object)__instance != (Object)(object)Player.m_localPlayer)
			{
				return true;
			}

			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(Player), "PlacePiece")]
	internal static class PlacePiecePatch
	{
		private static bool Prefix(Player __instance)
		{
			if (PieceRepeaterController.IsPlacingBatch)
			{
				return true;
			}

			if (!PipetteOwnership.BlocksVanillaPlacement())
			{
				return true;
			}

			if ((Object)(object)__instance != (Object)(object)Player.m_localPlayer)
			{
				return true;
			}

			return false;
		}
	}

	/// <summary>
	/// Block vanilla Attack placement path while pipette owns LMB (Dismantleheim pattern).
	/// Without this, first Attack after Instant tool select can arm/abort place-mode.
	/// </summary>
	[HarmonyPatch(typeof(Player), "PlayerAttackInput")]
	internal static class PlayerAttackInputPatch
	{
		private static bool Prefix(Player __instance)
		{
			if (!PipetteOwnership.BlocksVanillaPlacement())
			{
				return true;
			}

			if ((Object)(object)__instance != (Object)(object)Player.m_localPlayer)
			{
				return true;
			}

			return false;
		}
	}

	/// <summary>Block IH/vanilla MMB Remove while pipette is active.</summary>
	[HarmonyPatch(typeof(Player), "RemovePiece")]
	[HarmonyPriority(Priority.First)]
	internal static class PlayerRemovePiecePatch
	{
		private static bool Prefix(Player __instance)
		{
			if (PieceRepeaterController.OwnsMiddleClick(__instance))
			{
				SonicWorldToolsPlugin.ModLogger?.LogDebug("[PieceRepeater] Suppressed vanilla RemovePiece while sampling.");
				return false;
			}

			if (ScatterPropsCommand.ShouldSuppressMiddleClick(__instance))
			{
				return false;
			}

			if (!PipetteOwnership.BlocksVanillaPlacement())
			{
				return true;
			}

			if ((Object)(object)__instance != (Object)(object)Player.m_localPlayer)
			{
				return true;
			}

			return false;
		}
	}

	/// <summary>Block Use/Interact (chairs → sit, doors, etc.) while pipette is active.</summary>
	[HarmonyPatch(typeof(Player), "Interact")]
	internal static class PlayerInteractPatch
	{
		private static bool Prefix(Player __instance)
		{
			if (!PipetteOwnership.BlocksVanillaPlacement())
			{
				return true;
			}

			if ((Object)(object)__instance != (Object)(object)Player.m_localPlayer)
			{
				return true;
			}

			return false;
		}
	}

	/// <summary>
	/// Escape cancels pipette selection; do not also open the pause menu on that keypress.
	/// Idle at empty Corner A still allows Esc → pause.
	/// </summary>
	[HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
	internal static class MenuShowEscapePatch
	{
		private static bool Prefix()
		{
			PrecisionAreaPipetteController ctrl = PrecisionAreaPipetteController.Instance;
			if (ctrl != null && ctrl.ShouldSuppressPauseMenu)
			{
				return false;
			}

			if (PieceRepeaterController.ShouldSuppressPauseMenu)
			{
				return false;
			}

			return true;
		}
	}
}
