using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using SonicWorldTools.Integration;
using UnityEngine;

namespace SonicWorldTools.Modules.ScatterProps
{
	/// <summary>A sampled prop type in the Scatter Props palette.</summary>
	internal readonly struct ScatterSample
	{
		internal readonly string Prefab;
		internal readonly Vector3 Scale;
		/// <summary>Relative pick weight; 1 by default, set with <c>sonic_scatter weight</c>.</summary>
		internal readonly float Weight;

		internal ScatterSample(string prefab, Vector3 scale, float weight = 1f)
		{
			Prefab = prefab;
			Scale = scale;
			Weight = weight;
		}
	}

	/// <summary>
	/// Stores a small prefab palette, previews the next scatter at Infinity Hammer's cursor, and
	/// spawns exactly that layout as one World Edit Commands undo step.
	/// </summary>
	internal static class ScatterPropsCommand
	{
		internal const string CommandName = "sonic_scatter";
		private const float DefaultRadius = 3f;
		private const int ObjectsPerWheelNotch = 1;
		private const string CountDescriptionMarker = "\nAmount per click: ";
		private const float MaxSampleRayDistance = 80f;
		private static RaycastHit[] HitBuffer = new RaycastHit[64];
		private static Piece _countDescriptionPiece;
		private static int _countDescriptionValue = -1;
		private static bool _countDescriptionInitialized;
		private static int MaximumSamples => Mathf.Clamp(PluginConfig.ScatterMaxSamples?.Value ?? 8, 1, 32);
		private static int TotalObjects => Mathf.Clamp(PluginConfig.ScatterObjectsPerClick?.Value ?? 20, 1, 200);
		private static string SampleLabel => PluginConfig.FormatKeyLabel(PluginConfig.ScatterSampleKey?.Value ?? KeyCode.Mouse2);
		private static readonly List<ScatterSample> Samples = new List<ScatterSample>();
		private static bool _registered;
		private static bool _previewActive;

		/// <summary>
		/// Called from a Player.LateUpdate postfix, after the placement ghost is positioned for this
		/// frame (the same hook IH uses for its ruler). From the plugin's Update the ghost can still
		/// read as inactive, which hid the preview except while a menu froze placement.
		/// </summary>
		internal static void UpdatePreview(Player player)
		{
			if (!_previewActive)
			{
				ScatterPreview.Hide();
				return;
			}

			ScatterPreview.Update(player, Samples, TotalObjects);
		}

		internal static void Register()
		{
			if (_registered)
			{
				return;
			}

			_registered = true;
			new Terminal.ConsoleCommand(
				CommandName,
				"Sample networked props with the configured Scatter Props key; hold the clear modifier to empty the list.",
				HandleCommand);
		}

		internal static void Tick()
		{
			Player player = Player.m_localPlayer;
			bool inBuildMode = ToolRuntime.InBuildMode;
			bool hasSelectedTool = ToolRuntime.TryGetSelectedToolName(out string toolName);
			// IH can report its selected tool even when our separate equipped-state probe is false.
			// The selected Scatter Props tool is the authoritative activation signal here.
			bool active = player && inBuildMode && hasSelectedTool
			              && string.Equals(toolName, "Scatter Props", StringComparison.OrdinalIgnoreCase);
			bool mmbDown = ToolInput.WasMouseButtonDown(2);
			KeyCode sampleKey = PluginConfig.ScatterSampleKey != null
				? PluginConfig.ScatterSampleKey.Value
				: KeyCode.None;
			bool sampleKeyDown = sampleKey != KeyCode.None && ToolInput.WasBoundKeyOrMouseDown(sampleKey);
			if (mmbDown && PluginConfig.IsVerboseLogging())
			{
				bool hammerEquipped = ToolRuntime.InfinityHammerEquipped;
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[Scatter] MMB gate: player=" + (player ? "local" : "none")
					+ " buildMode=" + inBuildMode
					+ " IHEquipped=" + hammerEquipped
					+ " selected='" + (hasSelectedTool ? toolName : "<none>") + "'"
					+ " active=" + active
					+ " textBlocked=" + ToolRuntime.TextInputBlocked
					+ " sampleKey=" + sampleKey
					+ " sampleKeyDown=" + sampleKeyDown + ".");
			}
			_previewActive = active;
			if (!active)
			{
				ScatterPreview.Hide();
				_countDescriptionPiece = null;
				_countDescriptionValue = -1;
				_countDescriptionInitialized = false;
				return;
			}

			if (ToolRuntime.TextInputBlocked)
			{
				return;
			}

			float wheel = ToolInput.MouseScrollY;
			if (Mathf.Abs(wheel) > 0.01f && ToolInput.WasModifierHeld(KeyCode.LeftAlt))
			{
				int direction = wheel > 0f ? 1 : -1;
				int nextCount = Mathf.Clamp(
					PluginConfig.ScatterObjectsPerClick.Value + direction * ObjectsPerWheelNotch,
					1,
					200);
				if (nextCount != PluginConfig.ScatterObjectsPerClick.Value)
				{
					PluginConfig.ScatterObjectsPerClick.Value = nextCount;
					NotifyCenter("Scatter amount: " + nextCount + " per click.");
				}
				RefreshCountDescription();
				return;
			}

			if (!_countDescriptionInitialized || _countDescriptionValue != TotalObjects)
			{
				RefreshCountDescription();
			}

			if (!sampleKeyDown)
			{
				return;
			}

			if (PluginConfig.ScatterClearModifier.Value != KeyCode.None
			    && ToolInput.WasModifierHeld(PluginConfig.ScatterClearModifier.Value))
			{
				Samples.Clear();
				ScatterPreview.PruneTo(Samples);
				Notify("Scatter sample list cleared.");
				return;
			}

			if (!TryGetAimedPrefab(player, out string prefab, out Vector3 scale))
			{
				Notify("Aim at a networked prop and press " + SampleLabel + " to sample it.");
				return;
			}

			ToggleSample(prefab, scale);
		}

		private static void RefreshCountDescription()
		{
			if (!InfinityHammerBridge.TryGetActiveToolSelectionPiece("Scatter Props", out Piece piece) || !piece)
			{
				_countDescriptionPiece = null;
				_countDescriptionValue = -1;
				_countDescriptionInitialized = false;
				return;
			}
			if (piece == _countDescriptionPiece && _countDescriptionValue == TotalObjects)
			{
				return;
			}

			string description = piece.m_description ?? string.Empty;
			int existingCount = description.IndexOf(CountDescriptionMarker, StringComparison.Ordinal);
			if (existingCount >= 0)
			{
				description = description.Substring(0, existingCount);
			}

			piece.m_description = description + CountDescriptionMarker + TotalObjects;
			_countDescriptionPiece = piece;
			_countDescriptionValue = TotalObjects;
			_countDescriptionInitialized = true;
			try
			{
				MethodInfo refreshInfo = typeof(Hud).GetMethod(
					"SetupPieceInfo",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
					null,
					new[] { typeof(Piece) },
					null);
				if (Hud.instance && refreshInfo != null)
				{
					refreshInfo.Invoke(Hud.instance, new object[] { piece });
				}
			}
			catch (Exception ex)
			{
				LogVerbose("Could not refresh Scatter Props count in the IH tool panel: " + ex.Message);
			}
		}

		internal static bool ShouldSuppressMiddleClick(Player player)
		{
			return player == Player.m_localPlayer
			       && ToolRuntime.InBuildMode
			       && !ToolRuntime.TextInputBlocked
			       && PluginConfig.ScatterSampleKey.Value == KeyCode.Mouse2
			       && ToolInput.WasMouseButtonHeld(2)
			       && ToolRuntime.TryGetSelectedToolName(out string toolName)
			       && string.Equals(toolName, "Scatter Props", StringComparison.OrdinalIgnoreCase);
		}

		private static bool TryGetAimedPrefab(Player player, out string prefab, out Vector3 scale)
		{
			prefab = null;
			scale = Vector3.one;
			// Prefer Valheim's build-hover target. It is the same piece the hammer
			// highlights, while the raw ray can hit the player root's PlayerBase
			// collider and inherit that root's unrelated ZNetView.
			Piece hoveredPiece = player.GetHoveringPiece();
			bool verbose = PluginConfig.IsVerboseLogging();
			if (verbose)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[Scatter] Sample hover target: " + (hoveredPiece ? GetHierarchyPath(hoveredPiece.transform) : "<none>") + ".");
			}
			if (hoveredPiece && !IsOwnedByLocalPlayer(hoveredPiece.gameObject, player))
			{
				ZNetView hoveredView = hoveredPiece.GetComponent<ZNetView>();
				if (!hoveredView) hoveredView = hoveredPiece.GetComponentInParent<ZNetView>();
				if (hoveredView && !IsOwnedByLocalPlayer(hoveredView.gameObject, player))
				{
					prefab = ResolvePrefabId(hoveredView);
					if (!string.IsNullOrWhiteSpace(prefab))
					{
						scale = hoveredPiece.transform.lossyScale;
						LogVerbose(
							"[Scatter] Sample target from Player.GetHoveringPiece(): '" + hoveredPiece.name
							+ "' resolved to prefab '" + prefab + "' at scale " + FormatScale(scale) + ".");
						return true;
					}
				}
			}

			GameCamera camera = GameCamera.instance;
			if (!camera)
			{
				return false;
			}

			Ray ray = new Ray(camera.transform.position, camera.transform.forward);
			// Placement's ray mask is tailored to build surfaces. Sample against the
			// general world mask so trees and other non-piece props remain targetable.
			// The self-hierarchy check below keeps the player's own colliders out.
			int rayMask = Physics.DefaultRaycastLayers;

			int hitCount = Physics.RaycastNonAlloc(ray, HitBuffer, MaxSampleRayDistance, rayMask,
				QueryTriggerInteraction.Ignore);
			while (hitCount == HitBuffer.Length)
			{
				HitBuffer = new RaycastHit[HitBuffer.Length * 2];
				hitCount = Physics.RaycastNonAlloc(ray, HitBuffer, MaxSampleRayDistance, rayMask,
					QueryTriggerInteraction.Ignore);
			}
			Array.Sort(HitBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
			if (verbose)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[Scatter] Sample ray origin=" + FormatVector(ray.origin) + " direction=" + FormatVector(ray.direction)
					+ " mask=" + rayMask + " hitCount=" + hitCount + ".");
				for (int i = 0; i < hitCount; i++)
				{
					RaycastHit hit = HitBuffer[i];
					if (!hit.collider) continue;
					ZNetView hitView = hit.collider.GetComponentInParent<ZNetView>();
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
						"[Scatter] RayHit[" + i + "] distance=" + hit.distance.ToString("F2", CultureInfo.InvariantCulture)
						+ " point=" + FormatVector(hit.point)
						+ " layer='" + LayerMask.LayerToName(hit.collider.gameObject.layer)
						+ "' collider='" + GetHierarchyPath(hit.collider.transform)
						+ "' prefab='" + Utils.GetPrefabName(hit.collider.gameObject)
						+ "' znetview='" + (hitView ? GetHierarchyPath(hitView.transform) : "<none>") + "'.");
				}
			}
			for (int i = 0; i < hitCount; i++)
			{
				RaycastHit hit = HitBuffer[i];
				if (!hit.collider || IsOwnedByLocalPlayer(hit.collider.gameObject, player))
				{
					continue;
				}

				ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
				if (!view)
				{
					LogVerbose(
						"[Scatter] Sample ray stopped at nearest non-player collider '" + hit.collider.name
						+ "' (" + Utils.GetPrefabName(hit.collider.gameObject) + ") without a ZNetView; farther objects were not considered.");
					return false;
				}
				if (IsOwnedByLocalPlayer(view.gameObject, player))
				{
					// The collider can be outside the player's root while sharing a
					// parent ZNetView. Treat the player's own hierarchy as transparent.
					continue;
				}

				prefab = ResolvePrefabId(view);
				if (!string.IsNullOrWhiteSpace(prefab))
				{
					scale = view.transform.lossyScale;
					LogVerbose(
					"[Scatter] Sample target '" + hit.collider.name + "' resolved to prefab '" + prefab
						+ "' at scale " + FormatScale(scale) + ".");
					return true;
				}
				LogVerbose(
					"[Scatter] Sample ray stopped at nearest networked collider '" + hit.collider.name
					+ "' because its prefab could not be resolved; farther objects were not considered.");
				return false;
			}

			return false;
		}

		private static string GetHierarchyPath(Transform transform)
		{
			string path = string.Empty;
			while (transform)
			{
				path = string.IsNullOrEmpty(path) ? transform.name : transform.name + "/" + path;
				transform = transform.parent;
			}
			return path;
		}

		private static bool IsOwnedByLocalPlayer(GameObject target, Player player)
		{
			if (!target || !player) return false;
			Transform current = target.transform;
			while (current)
			{
				if (current == player.transform) return true;
				if (current.GetComponent<Player>() == player) return true;
				if (current.GetComponent<Character>() == player) return true;
				current = current.parent;
			}
			return false;
		}

		private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
		{
			internal static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
			public int Compare(RaycastHit left, RaycastHit right) => left.distance.CompareTo(right.distance);
		}

		private static string ResolvePrefabId(ZNetView view)
		{
			try
			{
				ZDO zdo = view.GetZDO();
				if (zdo != null && ZNetScene.instance)
				{
					GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
					if (prefab)
					{
						return Utils.GetPrefabName(prefab);
					}
				}
			}
			catch (Exception ex)
			{
				LogVerbose("[Scatter] ZDO prefab lookup fell back to object name: " + ex.Message);
			}

			return Utils.GetPrefabName(view.gameObject);
		}

		private static void HandleCommand(Terminal.ConsoleEventArgs args)
		{
			if (args.Length < 2)
			{
				Notify(args, "Scatter Props: click to scatter (" + TotalObjects + " props); Alt+wheel changes amount; radius wheel follows Infinity Hammer's bind; " + SampleLabel + " samples; "
				             + PluginConfig.FormatKeyLabel(PluginConfig.ScatterClearModifier.Value) + "+" + SampleLabel + " clears.");
				return;
			}

			string action = args[1].ToLowerInvariant();
			if (action == "sample")
			{
				Sample(args);
				return;
			}

			if (action == "clear")
			{
				Samples.Clear();
				ScatterPreview.PruneTo(Samples);
				Notify(args, "Scatter samples cleared.");
				return;
			}

			if (action == "place")
			{
				Place(args);
				return;
			}

			if (action == "weight")
			{
				SetWeight(args);
				return;
			}

			Notify(args, "Scatter Props: unknown action '" + args[1] + "'.");
		}

		private static void Sample(Terminal.ConsoleEventArgs args)
		{
			if (args.Length < 3 || string.IsNullOrWhiteSpace(args[2]))
			{
				Notify(args, "Aim at a prop to sample it.");
				return;
			}

			ToggleSample(args[2], Vector3.one);
		}

		/// <summary><c>sonic_scatter weight</c> lists weights; <c>sonic_scatter weight &lt;prefab&gt; &lt;n&gt;</c> sets one (0-100).</summary>
		private static void SetWeight(Terminal.ConsoleEventArgs args)
		{
			if (args.Length < 4)
			{
				if (Samples.Count == 0)
				{
					Notify(args, "Scatter Props has no samples. Sample props first, then: sonic_scatter weight <prefab> <weight>.");
					return;
				}
				var parts = new List<string>();
				foreach (ScatterSample s in Samples) parts.Add(s.Prefab + "=" + s.Weight.ToString("0.##", CultureInfo.InvariantCulture));
				Notify(args, "Scatter weights: " + string.Join(", ", parts) + ". Set with: sonic_scatter weight <prefab> <weight>.");
				return;
			}

			int index = Samples.FindIndex(s => string.Equals(s.Prefab, args[2], StringComparison.OrdinalIgnoreCase));
			if (index < 0)
			{
				Notify(args, "'" + args[2] + "' is not in the Scatter Props samples.");
				return;
			}
			if (!TryCoordinate(args[3], out float weight) || weight < 0f || weight > 100f)
			{
				Notify(args, "Weight must be a number from 0 to 100 (0 keeps the sample but never picks it).");
				return;
			}

			ScatterSample sample = Samples[index];
			Samples[index] = new ScatterSample(sample.Prefab, sample.Scale, weight);
			Notify(args, "Scatter weight for " + sample.Prefab + " set to " + weight.ToString("0.##", CultureInfo.InvariantCulture) + ".");
		}

		private static void ToggleSample(string prefab, Vector3 scale)
		{
			int existing = Samples.FindIndex(s => string.Equals(s.Prefab, prefab, StringComparison.OrdinalIgnoreCase));
			if (existing >= 0)
			{
				Samples.RemoveAt(existing);
				ScatterPreview.PruneTo(Samples);
				LogVerbose(
					"[Scatter] Removed sample '" + prefab + "'; palette now has " + Samples.Count + " sample(s).");
				Notify("Removed " + prefab + " from Scatter Props (" + Samples.Count + " sampled).");
				return;
			}

			if (Samples.Count >= MaximumSamples)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"[Scatter] Could not add sample '" + prefab + "'; palette is at its " + MaximumSamples + " type limit.");
				Notify("Scatter Props holds up to " + MaximumSamples + " prop types. Press " + SampleLabel + " on a sampled prop to remove it.");
				return;
			}

			Samples.Add(new ScatterSample(prefab, scale));
			LogVerbose(
				"[Scatter] Added sample '" + prefab + "' at scale " + FormatScale(scale)
				+ "; palette now has " + Samples.Count + " sample(s).");
			Notify("Sampled " + prefab + " at scale " + FormatScale(scale) + " (" + Samples.Count + "/" + MaximumSamples + "). Press " + SampleLabel + " again to remove.");
		}

		private static void Place(Terminal.ConsoleEventArgs args)
		{
			if (args.Length < 3)
			{
				Notify(args, "Scatter Props needs a target position.");
				return;
			}

			string[] coordinates = args[2].Split(',');
			if (coordinates.Length != 2
			    || !TryCoordinate(coordinates[0], out float x)
			    || !TryCoordinate(coordinates[1], out float z))
			{
				Notify(args, "Scatter Props received an invalid target position.");
				return;
			}

			float radius = ReadRadius(args);
			Player player = Player.m_localPlayer;
			IReadOnlyList<ScatterSample> palette = Samples;
			if (Samples.Count == 0)
			{
				if (!player || !TryGetAimedPrefab(player, out string hoveredPrefab, out Vector3 hoveredScale))
				{
					Notify(args, "Sample props with " + SampleLabel + ", or aim at a prop before clicking to scatter it.");
					return;
				}

				palette = new[] { new ScatterSample(hoveredPrefab, hoveredScale) };
			}

			IReadOnlyList<PlannedScatterObject> plan = ScatterPreview.GetPlan(player, x, z, radius, palette, TotalObjects);
			int spawned = SpawnPlan(plan, palette, out bool undoable);
			ScatterPreview.Reroll();
			LogVerbose("[Scatter] Spawned " + spawned + "/" + plan.Count + " at " + x.ToString("0.##", CultureInfo.InvariantCulture)
				+ "," + z.ToString("0.##", CultureInfo.InvariantCulture) + " radius=" + radius.ToString("0.##", CultureInfo.InvariantCulture)
				+ " undo=" + undoable + ".");
			Notify(args, "Scattered " + spawned + " prop(s)" + (undoable ? "; one undo reverts this click." : "; World Edit Commands undo is unavailable."));
		}

		/// <summary>
		/// Spawns every planned object inside one WEC UndoHelper action. WEC's ZNetView.Awake
		/// patch records each spawned ZDO while the action is open, so one undo removes the click.
		/// Spawning mirrors WEC's spawn_object: Instantiate, then ClientChanged on clients.
		/// </summary>
		private static int SpawnPlan(IReadOnlyList<PlannedScatterObject> plan, IReadOnlyList<ScatterSample> palette, out bool undoable)
		{
			undoable = WorldEditUndoBridge.IsAvailable;
			if (!ZNetScene.instance || plan.Count == 0) return 0;
			int spawned = 0;
			if (undoable) WorldEditUndoBridge.BeginAction();
			try
			{
				for (int i = 0; i < plan.Count; i++)
				{
					PlannedScatterObject planned = plan[i];
					if (planned.SampleIndex < 0 || planned.SampleIndex >= palette.Count) continue;
					ScatterSample sample = palette[planned.SampleIndex];
					GameObject prefab = ZNetScene.instance.GetPrefab(sample.Prefab);
					if (!prefab) continue;
					try
					{
						GameObject instance = UnityEngine.Object.Instantiate(prefab, planned.Position, planned.Rotation);
						ZNetView view = instance.GetComponent<ZNetView>();
						ZDO zdo = view ? view.GetZDO() : null;
						if (view && view.m_syncInitialScale)
						{
							Vector3 scale = sample.Scale * planned.ScaleFactor;
							instance.transform.localScale = scale;
							zdo?.Set(ZDOVars.s_scaleHash, scale);
						}
						if (zdo != null && ZNet.instance && !ZNet.instance.IsServer())
							ZDOMan.instance.ClientChanged(zdo.m_uid);
						spawned++;
					}
					catch (Exception ex)
					{
						SonicWorldToolsPlugin.ModLogger?.LogWarning("[Scatter] Could not spawn '" + sample.Prefab + "': " + ex.Message);
					}
				}
			}
			finally
			{
				if (undoable) WorldEditUndoBridge.EndAction();
			}

			return spawned;
		}

		/// <summary>
		/// The scale a spawned copy will actually have. Only prefabs with ZNetView.m_syncInitialScale
		/// persist a custom scale (same rule WEC's spawn_object follows without World Edit Tweaks).
		/// </summary>
		internal static Vector3 GetAppliedScale(ScatterSample sample, float scaleFactor)
		{
			GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(sample.Prefab) : null;
			if (!prefab) return sample.Scale * scaleFactor;
			ZNetView view = prefab.GetComponent<ZNetView>();
			return view && view.m_syncInitialScale ? sample.Scale * scaleFactor : prefab.transform.localScale;
		}

		private static void LogVerbose(string message)
		{
			if (PluginConfig.IsVerboseLogging())
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(message);
			}
		}

		private static string FormatScale(Vector3 scale)
		{
			return scale.x.ToString(CultureInfo.InvariantCulture) + ","
			       + scale.z.ToString(CultureInfo.InvariantCulture) + ","
			       + scale.y.ToString(CultureInfo.InvariantCulture);
		}

		private static string FormatVector(Vector3 value)
		{
			return string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##})", value.x, value.y, value.z);
		}

		private static float ReadRadius(Terminal.ConsoleEventArgs args)
		{
			for (int i = 3; i < args.Length; i++)
			{
				if (args[i].StartsWith("radius=", StringComparison.OrdinalIgnoreCase)
				    && TryCoordinate(args[i].Substring("radius=".Length), out float radius)
				    && radius > 0f)
				{
					return radius;
				}
			}

			return DefaultRadius;
		}

		private static bool TryCoordinate(string value, out float result)
		{
			return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
			       && !float.IsNaN(result)
			       && !float.IsInfinity(result);
		}

		private static void Notify(Terminal.ConsoleEventArgs args, string message)
		{
			args.Context.AddString(message);
			Notify(message);
		}

		private static void Notify(string message)
		{
			if (Player.m_localPlayer)
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message, 0, null);
			}
		}

		private static void NotifyCenter(string message)
		{
			if (Player.m_localPlayer)
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, message, 0, null);
			}
		}
	}
}
