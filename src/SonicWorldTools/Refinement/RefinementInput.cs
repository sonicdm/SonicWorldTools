using SonicWorldTools.Config;
using SonicWorldTools.Modules.PrecisionAreaPipette;
using UnityEngine;

namespace SonicWorldTools.Refinement
{
	/// <summary>
	/// Refinement gestures while Precision Area Pipette is Locked (§12).
	/// Dedicated AddPieceKey / ExcludePieceKey (optional RefineModifierKey chord).
	/// </summary>
	internal static class RefinementInput
	{
		internal readonly struct FrameInput
		{
			internal readonly bool ModifierHeld;

			internal readonly bool AddDown;

			internal readonly bool ExcludeDown;

			internal readonly bool HasHoverPiece;

			internal readonly Piece HoverPiece;

			internal FrameInput(bool modifierHeld, bool addDown, bool excludeDown, Piece hoverPiece)
			{
				ModifierHeld = modifierHeld;
				AddDown = addDown;
				ExcludeDown = excludeDown;
				HasHoverPiece = (Object)(object)hoverPiece != (Object)null;
				HoverPiece = hoverPiece;
			}
		}

		/// <summary>
		/// Sample refinement edges for this frame. Does not apply changes — controller owns state.
		/// </summary>
		internal static FrameInput Sample()
		{
			if (PrecisionAreaPipetteInput.IsTextInputBlocking())
			{
				return default;
			}

			KeyCode mod = PluginConfig.RefineModifierKey != null
				? PluginConfig.RefineModifierKey.Value
				: KeyCode.None;
			bool requireMod = mod != KeyCode.None;
			bool modifier = !requireMod || PrecisionAreaPipetteInput.WasRefineModifierHeld();
			Piece hover = TryGetHoverPiece();
			bool add = modifier && PrecisionAreaPipetteInput.WasAddPieceDown();
			bool exclude = modifier && PrecisionAreaPipetteInput.WasExcludePieceDown();
			return new FrameInput(modifier, add, exclude, hover);
		}

		private static Piece TryGetHoverPiece()
		{
			Player player = Player.m_localPlayer;
			if ((Object)(object)player == (Object)null)
			{
				return null;
			}

			return player.GetHoveringPiece();
		}
	}
}
