using System.Collections.Generic;
using SonicWorldTools.Config;
using UnityEngine;

namespace SonicWorldTools.Rendering
{
	/// <summary>
	/// Highlights build pieces via WearNTear + MaterialMan tints (§8, §19).
	/// Distinct states: volume base, manual add, manual exclude, hover.
	/// </summary>
	internal static class ObjectHighlightRenderer
	{
		// Soft tints — avoid WearNTear.Highlight neon green wash (keeps geometry readable).
		private static readonly Color BaseColor = new Color(0.45f, 0.78f, 0.95f, 1f);

		private static readonly Color AddedColor = new Color(0.45f, 0.92f, 0.55f, 1f);

		private static readonly Color ExcludedColor = new Color(0.95f, 0.42f, 0.38f, 1f);

		private static readonly Color HoverColor = new Color(0.98f, 0.88f, 0.4f, 1f);

		private static readonly List<WearNTear> _highlighted = new List<WearNTear>(128);

		private static readonly HashSet<int> _highlightedIds = new HashSet<int>();

		private static readonly HashSet<int> _activeTintIds = new HashSet<int>();

		private static readonly HashSet<int> _frameTintIds = new HashSet<int>();

		private static readonly Dictionary<int, GameObject> _tintRoots = new Dictionary<int, GameObject>();

		/// <summary>
		/// Replace the highlighted set (base / effective only). Null/empty clears.
		/// Prefer <see cref="SetVisualStates"/> when add/exclude/hover colors are needed.
		/// </summary>
		internal static void SetHighlighted(IEnumerable<GameObject> roots)
		{
			SetVisualStates(roots, null, null, null);
		}

		/// <summary>
		/// Apply §8 visual states. Effective selection = base ∪ added (exclude is visual-only).
		/// Hover may overlap another set; hover tint wins for that frame.
		/// </summary>
		internal static void SetVisualStates(
			IEnumerable<GameObject> baseRoots,
			IEnumerable<GameObject> addedRoots,
			IEnumerable<GameObject> excludedRoots,
			GameObject hoverRoot)
		{
			_highlighted.Clear();
			_highlightedIds.Clear();
			_frameTintIds.Clear();

			if (!IsHighlightsEnabled())
			{
				ClearTints();
				return;
			}

			CollectAndTint(baseRoots, BaseColor);
			CollectAndTint(addedRoots, AddedColor);
			CollectAndTint(excludedRoots, ExcludedColor);

			if ((Object)(object)hoverRoot != (Object)null)
			{
				ApplyRoot(hoverRoot, HoverColor);
			}

			PruneStaleTints();
		}

		/// <summary>
		/// Keep-alive hook. Selection tints are reapplied each frame via <see cref="SetVisualStates"/>;
		/// no WearNTear.Highlight (that neon wash hides piece geometry).
		/// </summary>
		internal static void Tick()
		{
			// Intentionally empty — MaterialMan tints are refreshed from Preview each frame.
		}

		/// <summary>Clear tracked highlights and MaterialMan tints.</summary>
		internal static void Clear()
		{
			_highlighted.Clear();
			_highlightedIds.Clear();
			ClearTints();
		}

		private static void CollectAndTint(IEnumerable<GameObject> roots, Color color)
		{
			if (roots == null)
			{
				return;
			}

			foreach (GameObject root in roots)
			{
				ApplyRoot(root, color);
			}
		}

		private static void ApplyRoot(GameObject root, Color color)
		{
			if ((Object)(object)root == (Object)null)
			{
				return;
			}

			WearNTear wear = root.GetComponent<WearNTear>();
			if ((Object)(object)wear == (Object)null)
			{
				wear = root.GetComponentInChildren<WearNTear>();
			}

			GameObject tintRoot = (Object)(object)wear != (Object)null ? wear.gameObject : root;
			if ((Object)(object)wear != (Object)null)
			{
				int id = wear.GetInstanceID();
				if (_highlightedIds.Add(id))
				{
					_highlighted.Add(wear);
				}
			}

			ApplyMaterialTint(tintRoot, color);
		}

		private static void ApplyMaterialTint(GameObject go, Color color)
		{
			if ((Object)(object)go == (Object)null)
			{
				return;
			}

			try
			{
				MaterialMan man = MaterialMan.instance;
				if ((Object)(object)man == (Object)null)
				{
					return;
				}

				// Soft multiply — readable geometry over neon flood.
				man.SetValue(go, ShaderProps._EmissionColor, color * 0.22f, false);
				man.SetValue(go, ShaderProps._Color, Color.Lerp(Color.white, color, 0.45f), false);

				int instanceId = go.GetInstanceID();
				_frameTintIds.Add(instanceId);
				_tintRoots[instanceId] = go;
			}
			catch
			{
				// MaterialMan/ShaderProps shape differs — skip tint rather than break selection.
			}
		}

		private static void PruneStaleTints()
		{
			if (_activeTintIds.Count > 0)
			{
				List<int> stale = null;
				foreach (int instanceId in _activeTintIds)
				{
					if (!_frameTintIds.Contains(instanceId))
					{
						if (stale == null)
						{
							stale = new List<int>();
						}

						stale.Add(instanceId);
					}
				}

				if (stale != null)
				{
					for (int i = 0; i < stale.Count; i++)
					{
						ResetTracked(stale[i]);
					}
				}
			}

			_activeTintIds.Clear();
			foreach (int instanceId in _frameTintIds)
			{
				_activeTintIds.Add(instanceId);
			}
		}

		private static void ClearTints()
		{
			if (_activeTintIds.Count == 0 && _tintRoots.Count == 0)
			{
				_frameTintIds.Clear();
				return;
			}

			List<int> ids = new List<int>(_activeTintIds);
			for (int i = 0; i < ids.Count; i++)
			{
				ResetTracked(ids[i]);
			}

			_activeTintIds.Clear();
			_frameTintIds.Clear();
			_tintRoots.Clear();
		}

		private static void ResetTracked(int instanceId)
		{
			GameObject go;
			if (!_tintRoots.TryGetValue(instanceId, out go))
			{
				_activeTintIds.Remove(instanceId);
				return;
			}

			_tintRoots.Remove(instanceId);
			_activeTintIds.Remove(instanceId);

			if ((Object)(object)go == (Object)null)
			{
				return;
			}

			try
			{
				MaterialMan man = MaterialMan.instance;
				if ((Object)(object)man == (Object)null)
				{
					return;
				}

				man.ResetValue(go, ShaderProps._Color);
				man.ResetValue(go, ShaderProps._EmissionColor);
			}
			catch
			{
				// Ignore cleanup failures on destroyed objects.
			}
		}

		private static bool IsHighlightsEnabled()
		{
			return PluginConfig.ShowHighlights == null || PluginConfig.ShowHighlights.Value;
		}
	}
}
