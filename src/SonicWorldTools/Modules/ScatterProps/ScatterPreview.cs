using System;
using System.Collections.Generic;
using SonicWorldTools.Config;
using SonicWorldTools.Integration;
using HarmonyLib;
using SonicWorldTools.Rendering;
using UnityEngine;

namespace SonicWorldTools.Modules.ScatterProps
{
	/// <summary>One object the next Scatter Props click will spawn.</summary>
	internal readonly struct PlannedScatterObject
	{
		internal readonly int SampleIndex;
		internal readonly Vector3 Position;
		internal readonly Quaternion Rotation;
		/// <summary>Multiplier on the sample's scale for this object.</summary>
		internal readonly float ScaleFactor;

		internal PlannedScatterObject(int sampleIndex, Vector3 position, Quaternion rotation, float scaleFactor)
		{
			SampleIndex = sampleIndex;
			Position = position;
			Rotation = rotation;
			ScaleFactor = scaleFactor;
		}
	}

	/// <summary>
	/// Owns the pending scatter layout and draws it as translucent ghosts at Infinity Hammer's
	/// cursor and radius. Placement asks for the same plan, so what is previewed is what spawns.
	/// </summary>
	internal static class ScatterPreview
	{
		private const float MoveEpsilon = 0.02f;
		/// <summary>How far above the terrain/cursor a surface may be and still count as the floor (rocks, build floors), so tree canopies are skipped.</summary>
		private const float SurfaceProbeHeight = 2f;

		private static readonly Dictionary<string, List<GameObject>> GhostPool = new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, int> GhostsUsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private static readonly List<PlannedScatterObject> Plan = new List<PlannedScatterObject>();
		private static readonly System.Random SeedSource = new System.Random();

		private static GameObject _root;
		private static Material _material;
		private static ScatterSlot[] _slots = Array.Empty<ScatterSlot>();
		private static int _seed = SeedSource.Next();
		private static string _layoutKey;
		private static string _orientationKey;
		/// <summary>Half the distance between height samples used to estimate the ground slope.</summary>
		private const float SlopeSampleOffset = 0.5f;
		private static bool _planValid;
		private static Vector3 _planCenter;
		private static float _planRadius;
		private static bool _visible;
		private static readonly List<LineRenderer> Markers = new List<LineRenderer>();
		private const float MarkerHeight = 1.5f;
		private const float MarkerWidth = 0.08f;
		private static string _lastHideReason;
		private static float _nextPlanLogTime;

		/// <summary>Pick a new random layout for the next click.</summary>
		internal static void Reroll()
		{
			_seed = SeedSource.Next();
			_layoutKey = null;
			_planValid = false;
		}

		/// <summary>Per-frame update while Scatter Props is the selected tool.</summary>
		internal static void Update(Player player, IReadOnlyList<ScatterSample> palette, int count)
		{
			string reason = null;
			Vector3 center = Vector3.zero;
			float radius = 0f;
			if (PluginConfig.ScatterShowPreview == null || !PluginConfig.ScatterShowPreview.Value) reason = "disabled in config";
			else if (palette.Count == 0) reason = "no samples";
			else if (!TryGetCursor(player, out center)) reason = "no IH cursor (ghost hidden, build menu open, or not placing)";
			else if (!InfinityHammerBridge.TryGetToolRadius(out radius)) reason = "IH tool radius unavailable";
			if (reason != null)
			{
				Hide(reason);
				return;
			}

			_lastHideReason = null;
			bool wasVisible = _visible;
			EnsurePlan(center, radius, palette, count);
			Render(palette);
			if (!wasVisible) LogVerbose("[Scatter] Preview shown: " + Plan.Count + " object(s), centre=" + center.ToString("F1") + " radius=" + radius.ToString("0.##") + ".");
		}

		/// <summary>
		/// The plan for a click at (x, z). Reuses the previewed positions when the click matches
		/// the cursor the preview was built for; otherwise resolves the same layout fresh.
		/// </summary>
		internal static IReadOnlyList<PlannedScatterObject> GetPlan(Player player, float x, float z, float radius, IReadOnlyList<ScatterSample> palette, int count)
		{
			float referenceY = player ? player.transform.position.y : 0f;
			// IH substitutes <x>,<z> from the same ghost the preview follows, so a matching
			// centre means the click is for the previewed plan; keep its height.
			if (_planValid && Mathf.Abs(_planCenter.x - x) < 1f && Mathf.Abs(_planCenter.z - z) < 1f)
			{
				referenceY = _planCenter.y;
			}
			else if (TryGetCursor(player, out Vector3 cursor)
			    && Mathf.Abs(cursor.x - x) < 1f && Mathf.Abs(cursor.z - z) < 1f)
			{
				referenceY = cursor.y;
			}

			EnsurePlan(new Vector3(x, referenceY, z), radius, palette, count);
			return Plan;
		}

		internal static void Hide(string reason = "Scatter Props not selected")
		{
			if (reason != _lastHideReason)
			{
				_lastHideReason = reason;
				LogVerbose("[Scatter] Preview hidden: " + reason + ".");
			}
			if (!_visible) return;
			_visible = false;
			for (int i = 0; i < Markers.Count; i++)
				if (Markers[i]) Markers[i].enabled = false;
			foreach (List<GameObject> ghosts in GhostPool.Values)
				for (int i = 0; i < ghosts.Count; i++)
					if (ghosts[i]) ghosts[i].SetActive(false);
		}

		internal static void Shutdown()
		{
			foreach (List<GameObject> ghosts in GhostPool.Values)
				for (int i = 0; i < ghosts.Count; i++)
					if (ghosts[i]) UnityEngine.Object.Destroy(ghosts[i]);
			GhostPool.Clear();
			GhostsUsed.Clear();
			Markers.Clear();
			if (_root) UnityEngine.Object.Destroy(_root);
			if (_material) UnityEngine.Object.Destroy(_material);
			_root = null;
			_material = null;
			_visible = false;
			_planValid = false;
			Plan.Clear();
		}

		/// <summary>Mirror IH's ruler visibility: the ghost exists, is shown, and the build menu is closed.</summary>
		private static bool TryGetCursor(Player player, out Vector3 center)
		{
			center = Vector3.zero;
			if (!player || !player.InPlaceMode() || Hud.IsPieceSelectionVisible()) return false;
			if (!InfinityHammerBridge.TryGetPlacementGhost(player, out GameObject ghost) || !ghost.activeInHierarchy) return false;
			center = ghost.transform.position;
			return true;
		}

		private static void EnsurePlan(Vector3 center, float radius, IReadOnlyList<ScatterSample> palette, int count)
		{
			var weights = new float[palette.Count];
			for (int i = 0; i < weights.Length; i++) weights[i] = palette[i].Weight;
			var options = new ScatterLayoutOptions
			{
				Count = count,
				Seed = _seed,
				Weights = weights,
				MinSpacing = PluginConfig.ScatterMinSpacing?.Value ?? 0f,
				Radius = radius,
				ScaleMin = PluginConfig.ScatterScaleMin?.Value ?? 1f,
				ScaleMax = PluginConfig.ScatterScaleMax?.Value ?? 1f,
			};
			string key = options.Key();
			if (key != _layoutKey)
			{
				_slots = ScatterLayout.Generate(options);
				_layoutKey = key;
				_planValid = false;
			}

			float maxTilt = PluginConfig.ScatterAlignToSlope != null && PluginConfig.ScatterAlignToSlope.Value
				? PluginConfig.ScatterMaxSlopeTilt.Value
				: 0f;
			string orientationKey = maxTilt.ToString("R");
			if (orientationKey != _orientationKey)
			{
				_orientationKey = orientationKey;
				_planValid = false;
			}

			if (_planValid
			    && Mathf.Abs(_planRadius - radius) < 0.001f
			    && (new Vector2(_planCenter.x - center.x, _planCenter.z - center.z)).sqrMagnitude < MoveEpsilon * MoveEpsilon
			    && Mathf.Abs(_planCenter.y - center.y) < 0.5f)
			{
				return;
			}

			Plan.Clear();
			for (int i = 0; i < _slots.Length; i++)
			{
				ScatterSlot slot = _slots[i];
				Vector3 position = ScatterLayout.ToWorld(slot, center, radius);
				position.y = ResolveFloor(position, center.y);
				Vector3 normal = maxTilt > 0f ? EstimateGroundNormal(position, center.y) : Vector3.up;
				Quaternion rotation = ScatterLayout.Orientation(slot.YawDegrees, normal, maxTilt);
				Plan.Add(new PlannedScatterObject(slot.SampleIndex, position, rotation, slot.ScaleFactor));
			}

			_planCenter = center;
			_planRadius = radius;
			_planValid = true;
			if (Time.unscaledTime >= _nextPlanLogTime && Plan.Count > 0)
			{
				_nextPlanLogTime = Time.unscaledTime + 1f;
				LogVerbose("[Scatter] Plan: centre=" + center.ToString("F1") + " radius=" + radius.ToString("0.##")
					+ " count=" + Plan.Count + "/" + count + " first=" + Plan[0].Position.ToString("F1")
					+ " spacing=" + options.MinSpacing.ToString("0.##") + " scale=" + options.ScaleMin.ToString("0.##") + "-" + options.ScaleMax.ToString("0.##")
					+ " tilt=" + maxTilt.ToString("0.#") + ".");
			}
		}

		/// <summary>
		/// Terrain height, raised to a solid surface (rock, build floor) only when that surface is
		/// within <see cref="SurfaceProbeHeight"/> of the terrain or cursor, so props do not land in tree canopies.
		/// </summary>
		private static float ResolveFloor(Vector3 position, float referenceY)
		{
			ZoneSystem zones = ZoneSystem.instance;
			if (!zones) return referenceY;
			float terrain = zones.GetGroundHeight(position);
			if (float.IsNaN(terrain) || float.IsInfinity(terrain)) terrain = referenceY;
			float probeTop = Mathf.Max(terrain, referenceY) + SurfaceProbeHeight;
			if (zones.GetSolidHeight(new Vector3(position.x, probeTop, position.z), out float solid, 0)
			    && !float.IsNaN(solid) && solid >= terrain - 0.05f && solid <= probeTop)
			{
				return solid;
			}

			return terrain;
		}

		/// <summary>Ground normal from floor heights a short step either side, so it follows terrain and floors alike.</summary>
		private static Vector3 EstimateGroundNormal(Vector3 position, float referenceY)
		{
			float d = SlopeSampleOffset;
			float east = ResolveFloor(position + new Vector3(d, 0f, 0f), referenceY);
			float west = ResolveFloor(position - new Vector3(d, 0f, 0f), referenceY);
			float north = ResolveFloor(position + new Vector3(0f, 0f, d), referenceY);
			float south = ResolveFloor(position - new Vector3(0f, 0f, d), referenceY);
			var normal = new Vector3(-(east - west) / (2f * d), 1f, -(north - south) / (2f * d));
			return normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
		}

		private static void Render(IReadOnlyList<ScatterSample> palette)
		{
			if (!ZNetScene.instance) { Hide(); return; }
			EnsureRoot();
			GhostsUsed.Clear();
			for (int i = 0; i < Plan.Count; i++)
			{
				PlannedScatterObject planned = Plan[i];
				if (planned.SampleIndex < 0 || planned.SampleIndex >= palette.Count) continue;
				ScatterSample sample = palette[planned.SampleIndex];
				GameObject ghost = TakeGhost(sample.Prefab);
				if (!ghost) continue;
				ghost.transform.SetPositionAndRotation(planned.Position, planned.Rotation);
				ghost.transform.localScale = ScatterPropsCommand.GetAppliedScale(sample, planned.ScaleFactor);
				if (!ghost.activeSelf) ghost.SetActive(true);
			}

			for (int i = 0; i < Plan.Count; i++)
			{
				LineRenderer marker = TakeMarker(i);
				Vector3 foot = Plan[i].Position;
				marker.SetPosition(0, foot);
				marker.SetPosition(1, foot + Vector3.up * MarkerHeight);
				marker.enabled = true;
			}
			for (int i = Plan.Count; i < Markers.Count; i++)
				if (Markers[i]) Markers[i].enabled = false;

			foreach (KeyValuePair<string, List<GameObject>> entry in GhostPool)
			{
				GhostsUsed.TryGetValue(entry.Key, out int used);
				for (int i = used; i < entry.Value.Count; i++)
					if (entry.Value[i] && entry.Value[i].activeSelf) entry.Value[i].SetActive(false);
			}

			_visible = true;
		}

		private static GameObject TakeGhost(string prefabName)
		{
			if (!GhostPool.TryGetValue(prefabName, out List<GameObject> ghosts))
			{
				ghosts = new List<GameObject>();
				GhostPool[prefabName] = ghosts;
			}

			GhostsUsed.TryGetValue(prefabName, out int used);
			GhostsUsed[prefabName] = used + 1;
			if (used < ghosts.Count && ghosts[used]) return ghosts[used];

			GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
			if (!prefab) return null;
			GameObject ghost = PrefabGhostFactory.Create(prefab, _root.transform, _material, "ScatterPropsGhost");
			if (used < ghosts.Count) ghosts[used] = ghost;
			else ghosts.Add(ghost);
			return ghost;
		}

		/// <summary>A vertical post at each planned spot, so the layout reads clearly whatever the prop's size.</summary>
		private static LineRenderer TakeMarker(int index)
		{
			while (Markers.Count <= index) Markers.Add(null);
			if (Markers[index]) return Markers[index];
			var go = new GameObject("ScatterPropsMarker") { hideFlags = HideFlags.HideAndDontSave };
			go.transform.SetParent(_root.transform, false);
			var line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.material = _material;
			line.startColor = line.endColor = new Color(0.25f, 0.9f, 1f, 0.95f);
			line.startWidth = line.endWidth = MarkerWidth;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			Markers[index] = line;
			return line;
		}

		private static void LogVerbose(string message)
		{
			if (PluginConfig.IsVerboseLogging()) SonicWorldToolsPlugin.ModLogger?.LogInfo(message);
		}

		private static void EnsureRoot()
		{
			if (_root) return;
			_root = new GameObject("SonicWorldTools_ScatterPreview") { hideFlags = HideFlags.HideAndDontSave };
			UnityEngine.Object.DontDestroyOnLoad(_root);
			Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
			// Same translucent cyan as the Line Tool ghosts so previews read the same across tools.
			_material = new Material(shader) { color = new Color(0.35f, 0.9f, 1f, 0.42f), hideFlags = HideFlags.HideAndDontSave };
		}

		/// <summary>Drop pooled ghosts for prefabs no longer in the palette.</summary>
		internal static void PruneTo(IReadOnlyList<ScatterSample> palette)
		{
			var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < palette.Count; i++) keep.Add(palette[i].Prefab);
			var drop = new List<string>();
			foreach (KeyValuePair<string, List<GameObject>> entry in GhostPool)
			{
				if (keep.Contains(entry.Key)) continue;
				for (int i = 0; i < entry.Value.Count; i++)
					if (entry.Value[i]) UnityEngine.Object.Destroy(entry.Value[i]);
				drop.Add(entry.Key);
			}
			for (int i = 0; i < drop.Count; i++) GhostPool.Remove(drop[i]);
		}
	}

	[HarmonyPatch(typeof(Player), "LateUpdate")]
	internal static class ScatterPreviewLateUpdatePatch
	{
		private static void Postfix(Player __instance)
		{
			if (__instance != Player.m_localPlayer) return;
			try
			{
				ScatterPropsCommand.UpdatePreview(__instance);
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("[Scatter] Preview update failed: " + ex.Message);
				ScatterPreview.Hide("update error");
			}
		}
	}
}
