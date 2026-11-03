using SonicWorldTools.Config;
using SonicWorldTools.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SonicWorldTools.Rendering
{
	/// <summary>
	/// Non-opaque corner markers + AABB wire edges for precision selection (§8).
	/// Public draw/clear API for M3 controller / preview; no selection math.
	/// </summary>
	internal static class BoxRenderer
	{
		private const string RootName = "SonicWorldTools_BoxRenderer";

		private static readonly Color BoxColor = new Color(0.25f, 0.85f, 1f, 0.75f);

		private static readonly Color CornerAColor = new Color(0.2f, 1f, 0.45f, 0.9f);

		private static readonly Color CornerBColor = new Color(1f, 0.85f, 0.2f, 0.9f);

		private static readonly Color CornerCColor = new Color(1f, 0.45f, 0.85f, 0.9f);

		private static GameObject _root;

		private static LineRenderer[] _edges;

		private static LineRenderer[] _markerA;

		private static LineRenderer[] _markerB;

		private static LineRenderer[] _markerC;

		private static readonly Vector3[] CornerBuffer = new Vector3[8];

		private static Material _sharedMaterial;

		/// <summary>Draw or update the live/locked wire box from two corners (AABB).</summary>
		internal static void DrawBox(Vector3 cornerA, Vector3 cornerB)
		{
			DrawBox(SelectionVolume.FromCorners(cornerA, cornerB));
		}

		/// <summary>Draw or update the wire box from a volume (AABB or oriented).</summary>
		internal static void DrawBox(SelectionVolume volume)
		{
			if (!IsBoxEnabled())
			{
				HideBox();
				return;
			}

			Ensure();
			volume.GetWorldCorners(CornerBuffer);
			SetEdgePositionsFromCorners(CornerBuffer);
			SetEdgesVisible(true);
		}

		/// <summary>Corner A marker (green). Pass null to hide.</summary>
		internal static void DrawCornerA(Vector3? worldPosition)
		{
			if (!IsMarkersEnabled() || !worldPosition.HasValue)
			{
				SetMarkerVisible(_markerA, false);
				return;
			}

			Ensure();
			DrawCross(_markerA, worldPosition.Value, CornerAColor);
			SetMarkerVisible(_markerA, true);
		}

		/// <summary>Corner B marker (amber). Pass null to hide.</summary>
		internal static void DrawCornerB(Vector3? worldPosition)
		{
			if (!IsMarkersEnabled() || !worldPosition.HasValue)
			{
				SetMarkerVisible(_markerB, false);
				return;
			}

			Ensure();
			DrawCross(_markerB, worldPosition.Value, CornerBColor);
			SetMarkerVisible(_markerB, true);
		}

		/// <summary>Corner C marker (magenta). Pass null to hide.</summary>
		internal static void DrawCornerC(Vector3? worldPosition)
		{
			if (!IsMarkersEnabled() || !worldPosition.HasValue)
			{
				SetMarkerVisible(_markerC, false);
				return;
			}

			Ensure();
			DrawCross(_markerC, worldPosition.Value, CornerCColor);
			SetMarkerVisible(_markerC, true);
		}

		/// <summary>Hide box + markers without destroying cached resources.</summary>
		internal static void Clear()
		{
			HideBox();
			SetMarkerVisible(_markerA, false);
			SetMarkerVisible(_markerB, false);
			SetMarkerVisible(_markerC, false);
		}

		/// <summary>Destroy all render GameObjects (plugin unload).</summary>
		internal static void Dispose()
		{
			Clear();
			if ((Object)(object)_root != (Object)null)
			{
				Object.Destroy(_root);
			}

			_root = null;
			_edges = null;
			_markerA = null;
			_markerB = null;
			_markerC = null;
			if ((Object)(object)_sharedMaterial != (Object)null)
			{
				Object.Destroy(_sharedMaterial);
				_sharedMaterial = null;
			}
		}

		private static bool IsBoxEnabled()
		{
			return PluginConfig.ShowBox == null || PluginConfig.ShowBox.Value;
		}

		private static bool IsMarkersEnabled()
		{
			return PluginConfig.ShowCornerMarkers == null || PluginConfig.ShowCornerMarkers.Value;
		}

		private static void HideBox()
		{
			SetEdgesVisible(false);
		}

		private static void Ensure()
		{
			if ((Object)(object)_root != (Object)null && _edges != null)
			{
				return;
			}

			_root = new GameObject(RootName);
			Object.DontDestroyOnLoad(_root);
			_root.hideFlags = HideFlags.HideAndDontSave;

			if ((Object)(object)_sharedMaterial == (Object)null)
			{
				Shader shader = Shader.Find("Sprites/Default");
				if ((Object)(object)shader == (Object)null)
				{
					shader = Shader.Find("Unlit/Color");
				}

				_sharedMaterial = (Object)(object)shader != (Object)null
					? new Material(shader)
					: new Material(Shader.Find("Hidden/InternalErrorShader"));
				_sharedMaterial.hideFlags = HideFlags.HideAndDontSave;
			}

			_edges = new LineRenderer[12];
			for (int i = 0; i < 12; i++)
			{
				_edges[i] = CreateLine("Edge_" + i, BoxColor, 0.04f);
			}

			_markerA = CreateCross("CornerA", CornerAColor);
			_markerB = CreateCross("CornerB", CornerBColor);
			_markerC = CreateCross("CornerC", CornerCColor);
			SetEdgesVisible(false);
			SetMarkerVisible(_markerA, false);
			SetMarkerVisible(_markerB, false);
			SetMarkerVisible(_markerC, false);
		}

		private static LineRenderer[] CreateCross(string name, Color color)
		{
			var lines = new LineRenderer[3];
			for (int i = 0; i < 3; i++)
			{
				lines[i] = CreateLine(name + "_" + i, color, 0.06f);
			}

			return lines;
		}

		private static LineRenderer CreateLine(string name, Color color, float width)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(_root.transform, false);
			LineRenderer lr = go.AddComponent<LineRenderer>();
			lr.sharedMaterial = _sharedMaterial;
			lr.startColor = color;
			lr.endColor = color;
			lr.widthMultiplier = width;
			lr.positionCount = 2;
			lr.useWorldSpace = true;
			lr.shadowCastingMode = ShadowCastingMode.Off;
			lr.receiveShadows = false;
			lr.loop = false;
			lr.numCapVertices = 2;
			lr.enabled = false;
			return lr;
		}

		private static void DrawCross(LineRenderer[] lines, Vector3 center, Color color)
		{
			if (lines == null)
			{
				return;
			}

			const float arm = 0.35f;
			SetSegment(lines[0], center + Vector3.left * arm, center + Vector3.right * arm, color);
			SetSegment(lines[1], center + Vector3.down * arm, center + Vector3.up * arm, color);
			SetSegment(lines[2], center + Vector3.back * arm, center + Vector3.forward * arm, color);
		}

		private static void SetSegment(LineRenderer lr, Vector3 a, Vector3 b, Color color)
		{
			if ((Object)(object)lr == (Object)null)
			{
				return;
			}

			lr.startColor = color;
			lr.endColor = color;
			lr.SetPosition(0, a);
			lr.SetPosition(1, b);
			lr.enabled = true;
		}

		private static void SetEdgesVisible(bool visible)
		{
			if (_edges == null)
			{
				return;
			}

			for (int i = 0; i < _edges.Length; i++)
			{
				if ((Object)(object)_edges[i] != (Object)null)
				{
					_edges[i].enabled = visible;
				}
			}
		}

		private static void SetMarkerVisible(LineRenderer[] lines, bool visible)
		{
			if (lines == null)
			{
				return;
			}

			for (int i = 0; i < lines.Length; i++)
			{
				if ((Object)(object)lines[i] != (Object)null)
				{
					lines[i].enabled = visible;
				}
			}
		}

		/// <summary>
		/// Corner order from <see cref="SelectionVolume.GetWorldCorners"/>:
		/// 0-3 bottom ring, 4-7 top ring (each ring: +X then +Z).
		/// </summary>
		private static void SetEdgePositionsFromCorners(Vector3[] c)
		{
			SetSegment(_edges[0], c[0], c[1], BoxColor);
			SetSegment(_edges[1], c[1], c[2], BoxColor);
			SetSegment(_edges[2], c[2], c[3], BoxColor);
			SetSegment(_edges[3], c[3], c[0], BoxColor);

			SetSegment(_edges[4], c[4], c[5], BoxColor);
			SetSegment(_edges[5], c[5], c[6], BoxColor);
			SetSegment(_edges[6], c[6], c[7], BoxColor);
			SetSegment(_edges[7], c[7], c[4], BoxColor);

			SetSegment(_edges[8], c[0], c[4], BoxColor);
			SetSegment(_edges[9], c[1], c[5], BoxColor);
			SetSegment(_edges[10], c[2], c[6], BoxColor);
			SetSegment(_edges[11], c[3], c[7], BoxColor);
		}
	}
}
