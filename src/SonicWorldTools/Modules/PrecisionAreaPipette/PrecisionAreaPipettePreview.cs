using System.Collections.Generic;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using SonicWorldTools.Rendering;
using UnityEngine;

namespace SonicWorldTools.Modules.PrecisionAreaPipette
{
	/// <summary>
	/// Orchestrates box + highlight preview from pipette phase / <see cref="SelectionState"/>.
	/// Preview set must match effective selection (M5 hand-off truth); excluded/hover are visual-only (§8).
	/// </summary>
	internal static class PrecisionAreaPipettePreview
	{
		private static readonly List<GameObject> _baseRoots = new List<GameObject>(128);

		private static readonly List<GameObject> _addedRoots = new List<GameObject>(64);

		private static readonly List<GameObject> _excludedRoots = new List<GameObject>(64);

		/// <summary>
		/// Draw markers/box for the current phase and apply visual states for base/add/exclude/hover.
		/// <paramref name="aimPoint"/> is the live hammer aim (Corner A ghost / live B or C).
		/// </summary>
		internal static void Draw(
			PrecisionAreaPipettePhase phase,
			Vector3? cornerA,
			Vector3? cornerB,
			Vector3? cornerC,
			SelectionState state,
			IReadOnlyDictionary<string, GameObject> keyToRoot,
			GameObject hoverRoot,
			Vector3? aimPoint = null)
		{
			switch (phase)
			{
				case PrecisionAreaPipettePhase.Idle:
					BoxRenderer.Clear();
					ObjectHighlightRenderer.Clear();
					return;

				case PrecisionAreaPipettePhase.AwaitingCornerA:
					BoxRenderer.Clear();
					ObjectHighlightRenderer.Clear();
					if (aimPoint.HasValue)
					{
						BoxRenderer.DrawCornerA(aimPoint);
					}

					return;

				case PrecisionAreaPipettePhase.AwaitingCornerB:
					BoxRenderer.Clear();
					ObjectHighlightRenderer.Clear();
					if (cornerA.HasValue)
					{
						BoxRenderer.DrawCornerA(cornerA);
						Vector3 b = aimPoint ?? cornerB ?? cornerA.Value;
						BoxRenderer.DrawCornerB(aimPoint ?? cornerB);
						// Temporary AABB ribbon while aiming the width edge.
						BoxRenderer.DrawBox(cornerA.Value, b);
					}

					return;

				case PrecisionAreaPipettePhase.AwaitingCornerC:
					ObjectHighlightRenderer.Clear();
					if (cornerA.HasValue && cornerB.HasValue)
					{
						BoxRenderer.DrawCornerA(cornerA);
						BoxRenderer.DrawCornerB(cornerB);
						Vector3 c = aimPoint ?? cornerC ?? cornerB.Value;
						BoxRenderer.DrawCornerC(aimPoint ?? cornerC);
						SelectionVolume live = SelectionVolume.FromThreePoints(
							cornerA.Value,
							cornerB.Value,
							c).Expanded(PluginConfig.GetSelectionPadding());
						BoxRenderer.DrawBox(live);
						if (state != null && state.HasCorners)
						{
							ApplyVisualStates(state, keyToRoot, hoverRoot);
						}
					}

					return;

				case PrecisionAreaPipettePhase.Locked:
					if (state != null && state.HasCorners)
					{
						BoxRenderer.DrawCornerA(cornerA);
						BoxRenderer.DrawCornerB(cornerB);
						BoxRenderer.DrawCornerC(cornerC);
						BoxRenderer.DrawBox(state.Volume);
						ApplyVisualStates(state, keyToRoot, hoverRoot);
					}

					return;
			}
		}

		/// <summary>Per-frame highlight keep-alive (§19).</summary>
		internal static void TickHighlights()
		{
			ObjectHighlightRenderer.Tick();
		}

		internal static void Clear()
		{
			BoxRenderer.Clear();
			ObjectHighlightRenderer.Clear();
			_baseRoots.Clear();
			_addedRoots.Clear();
			_excludedRoots.Clear();
		}

		internal static void Dispose()
		{
			Clear();
			BoxRenderer.Dispose();
		}

		private static void ApplyVisualStates(
			SelectionState state,
			IReadOnlyDictionary<string, GameObject> keyToRoot,
			GameObject hoverRoot)
		{
			if (PluginConfig.ShowHighlights != null && !PluginConfig.ShowHighlights.Value)
			{
				ObjectHighlightRenderer.Clear();
				return;
			}

			_baseRoots.Clear();
			_addedRoots.Clear();
			_excludedRoots.Clear();

			if (state == null || keyToRoot == null)
			{
				ObjectHighlightRenderer.SetVisualStates(_baseRoots, _addedRoots, _excludedRoots, null);
				return;
			}

			CollectRoots(state.BaseSelection, keyToRoot, _baseRoots);
			CollectRoots(state.AddedSelection, keyToRoot, _addedRoots);
			CollectRoots(state.ExcludedSelection, keyToRoot, _excludedRoots);

			ObjectHighlightRenderer.SetVisualStates(_baseRoots, _addedRoots, _excludedRoots, hoverRoot);
		}

		private static void CollectRoots(
			IReadOnlyCollection<string> keys,
			IReadOnlyDictionary<string, GameObject> keyToRoot,
			List<GameObject> into)
		{
			if (keys == null)
			{
				return;
			}

			foreach (string key in keys)
			{
				if (keyToRoot.TryGetValue(key, out GameObject root)
				    && (Object)(object)root != (Object)null)
				{
					into.Add(root);
				}
			}
		}
	}
}
