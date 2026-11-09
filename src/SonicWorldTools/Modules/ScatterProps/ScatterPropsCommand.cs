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
	/// <summary>Stores a small prefab palette and asks WEC to scatter a mixed batch.</summary>
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
		private static readonly List<SampledPrefab> Samples = new List<SampledPrefab>();
		private static bool _registered;

		private readonly struct SampledPrefab
		{
			internal readonly string Prefab;
			internal readonly Vector3 Scale;

			internal SampledPrefab(string prefab, Vector3 scale)
			{
				Prefab = prefab;
				Scale = scale;
			}
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
			bool active = player && ToolRuntime.InBuildMode && ToolRuntime.InfinityHammerEquipped
			              && ToolRuntime.TryGetSelectedToolName(out string toolName)
			              && string.Equals(toolName, "Scatter Props", StringComparison.OrdinalIgnoreCase);
			if (!active)
			{
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

			if (!ToolInput.WasBoundKeyOrMouseDown(PluginConfig.ScatterSampleKey.Value))
			{
				return;
			}

			if (PluginConfig.ScatterClearModifier.Value != KeyCode.None
			    && ToolInput.WasModifierHeld(PluginConfig.ScatterClearModifier.Value))
			{
				Samples.Clear();
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
				SonicWorldToolsPlugin.ModLogger?.LogDebug(
					"Could not refresh Scatter Props count in the IH tool panel: " + ex.Message);
			}
		}

		internal static bool ShouldSuppressMiddleClick(Player player)
		{
			return player == Player.m_localPlayer
			       && ToolRuntime.InBuildMode
			       && ToolRuntime.InfinityHammerEquipped
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
						SonicWorldToolsPlugin.ModLogger?.LogInfo(
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
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
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
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[Scatter] Sample target '" + hit.collider.name + "' resolved to prefab '" + prefab
						+ "' at scale " + FormatScale(scale) + ".");
					return true;
				}
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
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
				SonicWorldToolsPlugin.ModLogger?.LogDebug("[Scatter] ZDO prefab lookup fell back to object name: " + ex.Message);
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
				Notify(args, "Scatter samples cleared.");
				return;
			}

			if (action == "place")
			{
				Place(args);
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

		private static void ToggleSample(string prefab, Vector3 scale)
		{
			int existing = Samples.FindIndex(s => string.Equals(s.Prefab, prefab, StringComparison.OrdinalIgnoreCase));
			if (existing >= 0)
			{
				Samples.RemoveAt(existing);
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
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

			Samples.Add(new SampledPrefab(prefab, scale));
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
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
			if (Samples.Count == 0)
			{
				Player player = Player.m_localPlayer;
				if (!player || !TryGetAimedPrefab(player, out string hoveredPrefab, out Vector3 hoveredScale))
				{
					Notify(args, "Sample props with " + SampleLabel + ", or aim at a prop before clicking to scatter it.");
					return;
				}

				Spawn(args.Context, hoveredPrefab, hoveredScale, TotalObjects, x, z, radius);
				return;
			}

			// Choose a sample independently for every spawned object. A quotient /
			// remainder split gave all leftover objects to the earliest samples, so
			// with one object per click the first sample always won.
			int[] amounts = new int[Samples.Count];
			for (int i = 0; i < TotalObjects; i++)
				amounts[UnityEngine.Random.Range(0, Samples.Count)]++;

			for (int i = 0; i < Samples.Count; i++)
			{
				int amount = amounts[i];
				if (amount <= 0) continue;
				Spawn(args.Context, Samples[i].Prefab, Samples[i].Scale, amount, x, z, radius);
			}
		}

		private static void Spawn(Terminal context, string prefab, Vector3 scale, int amount, float x, float z, float radius)
		{
			string command = "spawn_object " + prefab
			                 + " amount=" + amount.ToString(CultureInfo.InvariantCulture)
			                 + " from=" + x.ToString(CultureInfo.InvariantCulture)
			                 + "," + z.ToString(CultureInfo.InvariantCulture)
			                 + " radius=" + radius.ToString(CultureInfo.InvariantCulture)
			                 + " rot=0-360 scale=" + FormatScale(scale);
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[Scatter] Running: " + command);
			context.TryRunCommand(command);
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
