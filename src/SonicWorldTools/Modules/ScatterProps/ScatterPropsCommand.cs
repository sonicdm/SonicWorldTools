using System;
using System.Collections.Generic;
using System.Globalization;
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
				return;
			}

			if (ToolRuntime.TextInputBlocked
			    || !ToolInput.WasBoundKeyOrMouseDown(PluginConfig.ScatterSampleKey.Value))
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
			if (hoveredPiece)
			{
				ZNetView hoveredView = hoveredPiece.GetComponent<ZNetView>();
				if (!hoveredView) hoveredView = hoveredPiece.GetComponentInParent<ZNetView>();
				if (hoveredView && !hoveredView.GetComponentInParent<Player>())
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
			RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, Physics.AllLayers, QueryTriggerInteraction.Collide);
			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			foreach (RaycastHit hit in hits)
			{
				if (!hit.collider
				    || hit.collider.transform == player.transform
				    || hit.collider.transform.IsChildOf(player.transform)
				    || hit.collider.GetComponentInParent<Player>())
				{
					continue;
				}

				ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
				if (!view)
				{
					continue;
				}
				if (view.transform == player.transform
				    || view.transform.IsChildOf(player.transform)
				    || view.GetComponentInParent<Player>())
				{
					SonicWorldToolsPlugin.ModLogger?.LogDebug(
						"[Scatter] Ignored player-owned ray hit '" + hit.collider.name + "'.");
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
			}

			return false;
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
				Notify(args, "Scatter Props: click to scatter; " + SampleLabel + " samples; "
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
				Notify("Removed " + prefab + " from Scatter Props (" + Samples.Count + " sampled).");
				return;
			}

			if (Samples.Count >= MaximumSamples)
			{
				Notify("Scatter Props holds up to " + MaximumSamples + " prop types. Press " + SampleLabel + " on a sampled prop to remove it.");
				return;
			}

			Samples.Add(new SampledPrefab(prefab, scale));
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

			int baseCount = TotalObjects / Samples.Count;
			int remainder = TotalObjects % Samples.Count;
			for (int i = 0; i < Samples.Count; i++)
			{
				int amount = baseCount + (i < remainder ? 1 : 0);
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
	}
}
