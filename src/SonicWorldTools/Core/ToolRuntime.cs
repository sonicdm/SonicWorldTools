using SonicWorldTools.Integration;
using UnityEngine;

namespace SonicWorldTools.Core
{
	/// <summary>
	/// Per-frame snapshot of player/tool state shared by tool modules. This is the
	/// single place that polls Infinity Hammer selection and player input readiness.
	/// </summary>
	internal static class ToolRuntime
	{
		private static int _frame = -1;
		private static bool _selectionRead;
		private static bool _hasSelection;
		private static string _selectionName;

		internal static Player LocalPlayer { get; private set; }
		internal static bool InBuildMode { get; private set; }
		internal static bool InfinityHammerEquipped { get; private set; }
		internal static bool TextInputBlocked { get; private set; }

		internal static void BeginFrame()
		{
			EnsureFrame();
			LocalPlayer = Player.m_localPlayer;
			InBuildMode = LocalPlayer && LocalPlayer.InPlaceMode();
			ItemDrop.ItemData rightItem = LocalPlayer
				? HarmonyLib.AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(LocalPlayer) as ItemDrop.ItemData
				: null;
			InfinityHammerEquipped = rightItem?.m_customData != null
			                         && rightItem.m_customData.ContainsKey("infinity_hammer");
			TextInputBlocked = ToolInput.IsTextInputBlocking();
		}

		internal static bool IsCurrentPlayer(Player player)
		{
			return player && player == (LocalPlayer ? LocalPlayer : Player.m_localPlayer);
		}

		internal static bool TryGetSelectedToolName(out string name)
		{
			EnsureFrame();
			if (!_selectionRead)
			{
				_selectionRead = true;
				_hasSelection = InfinityHammerBridge.TryGetActiveToolSelectionName(out _selectionName);
			}

			name = _selectionName;
			return _hasSelection;
		}

		/// <summary>Invalidate after an IH command or committed menu selection.</summary>
		internal static void InvalidateToolSelection()
		{
			_selectionRead = false;
			_hasSelection = false;
			_selectionName = null;
		}

		internal static void Reset()
		{
			_frame = -1;
			_selectionRead = false;
			_hasSelection = false;
			_selectionName = null;
			LocalPlayer = null;
			InBuildMode = false;
			InfinityHammerEquipped = false;
			TextInputBlocked = false;
		}

		private static void EnsureFrame()
		{
			int frame = Time.frameCount;
			if (_frame == frame) return;
			_frame = frame;
			_selectionRead = false;
			_hasSelection = false;
			_selectionName = null;
			ToolInput.BeginFrame();
		}
	}
}
