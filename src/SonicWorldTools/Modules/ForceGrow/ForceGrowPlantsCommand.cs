using System;
using System.Globalization;
using BepInEx;
using SonicWorldTools.Config;
using UnityEngine;

namespace SonicWorldTools.Modules.ForceGrow
{
	/// <summary>Matures healthy plants and hatches single incubating eggs in the selected radius.</summary>
	internal static class ForceGrowPlantsCommand
	{
		internal const string CommandName = "sonic_force_grow";
		private const float MaximumRadius = 50f;
		private static bool _registered;

		internal static void Register()
		{
			if (_registered) return;
			_registered = true;
			new Terminal.ConsoleCommand(
				CommandName,
				"Instantly mature healthy plants and hatch single eggs in the selected radius. Usage: sonic_force_grow x,z radius=<metres> <area>",
				HandleCommand);
		}

		private static void HandleCommand(Terminal.ConsoleEventArgs args)
		{
			if (!TryReadCenter(args, out Vector3 center) || !TryReadRadius(args, out float radius))
			{
				Notify(args.Context, "Force Grow could not read the Hoe area. Select Force Grow in the Infinity Hammer Hoe menu and click a target area.");
				return;
			}

			if (!ZNet.instance || !Player.m_localPlayer)
			{
				Notify(args.Context, "Force Grow requires a loaded world and local player.");
				return;
			}

			Plant[] plants = UnityEngine.Object.FindObjectsByType<Plant>(FindObjectsSortMode.None);
			float radiusSquared = radius * radius;
			int grown = 0;
			int hatched = 0;
			int stackedEggs = 0;
			int unhealthy = 0;
			int unavailable = 0;

			foreach (Plant plant in plants)
			{
				if (!plant || !InRadius(plant.transform.position, center, radiusSquared))
					continue;

				ZNetView view = plant.GetComponent<ZNetView>();
				if (!view || !view.IsValid() || view.GetZDO() == null)
				{
					unavailable++;
					continue;
				}

				// Recheck conditions now; unhealthy plants do not grow in vanilla and
				// may be destroyed by the normal growth update.
				plant.UpdateHealth(10.0);
				if (plant.GetStatus() != Plant.Status.Healthy)
				{
					unhealthy++;
					continue;
				}

				if (plant.m_grownPrefabs == null || plant.m_grownPrefabs.Length == 0)
				{
					unavailable++;
					continue;
				}

				// Plant.Grow is the game's own mature transition: it spawns the grown
				// prefab, applies scale/rotation/effects, and removes the sapling. Claim
				// ownership so the ZNetView performs that transition authoritatively.
				if (!view.IsOwner()) view.ClaimOwnership();
				if (!view.IsOwner())
				{
					unavailable++;
					continue;
				}

				string plantName = plant.name;
				try
				{
					if (plant.Grow()) grown++;
					else unavailable++;
				}
				catch (Exception ex)
				{
					unavailable++;
					SonicWorldToolsPlugin.ModLogger?.LogWarning("Force Grow could not mature '" + plantName + "': " + ex.Message);
				}
			}

			EggGrow[] eggs = UnityEngine.Object.FindObjectsByType<EggGrow>(FindObjectsSortMode.None);
			foreach (EggGrow egg in eggs)
			{
				if (!egg || !InRadius(egg.transform.position, center, radiusSquared))
					continue;

				ZNetView view = egg.GetComponent<ZNetView>();
				ItemDrop item = egg.GetComponent<ItemDrop>();
				if (!view || !view.IsValid() || view.GetZDO() == null || !item || item.m_itemData == null
				    || !egg.m_grownPrefab)
				{
					unavailable++;
					continue;
				}

				if (item.m_itemData.m_stack > 1)
				{
					stackedEggs++;
					continue;
				}

				if (!view.IsOwner()) view.ClaimOwnership();
				if (!view.IsOwner())
				{
					unavailable++;
					continue;
				}

				string eggName = egg.name;
				try
				{
					// Match EggGrow's vanilla hatch transition, but skip its incubation
					// timer and fire/roof checks because the player explicitly forced it.
					GameObject grownEgg = UnityEngine.Object.Instantiate(
						egg.m_grownPrefab,
						egg.transform.position,
						egg.transform.rotation);
					if (!grownEgg)
					{
						unavailable++;
						continue;
					}

					egg.m_hatchEffect?.Create(egg.transform.position, egg.transform.rotation);
					Character character = grownEgg.GetComponent<Character>();
					if (character)
					{
						character.SetTamed(egg.m_tamed);
						character.SetLevel(item.m_itemData.m_quality);
					}
					view.Destroy();
					hatched++;
				}
				catch (Exception ex)
				{
					unavailable++;
					SonicWorldToolsPlugin.ModLogger?.LogWarning("Force Grow could not hatch '" + eggName + "': " + ex.Message);
				}
			}

			string summary = "Force Grow: matured " + grown + " plant(s), hatched " + hatched + " egg(s)";
			if (unhealthy > 0) summary += "; " + unhealthy + " failed growth conditions";
			if (stackedEggs > 0) summary += "; " + stackedEggs + " stacked egg(s) skipped";
			if (unavailable > 0) summary += "; " + unavailable + " could not be updated";
			Notify(args.Context, summary + ".");
		}

		private static bool InRadius(Vector3 position, Vector3 center, float radiusSquared)
		{
			float dx = position.x - center.x;
			float dz = position.z - center.z;
			return dx * dx + dz * dz <= radiusSquared;
		}

		private static bool TryReadCenter(Terminal.ConsoleEventArgs args, out Vector3 center)
		{
			center = Vector3.zero;
			if (args == null || args.Length < 2) return false;
			string[] coordinates = args[1].Split(',');
			return coordinates.Length == 2
			       && TryCoordinate(coordinates[0], out float x)
			       && TryCoordinate(coordinates[1], out float z)
			       && SetCenter(x, z, out center);
		}

		private static bool SetCenter(float x, float z, out Vector3 center)
		{
			center = new Vector3(x, 0f, z);
			return true;
		}

		private static bool TryReadRadius(Terminal.ConsoleEventArgs args, out float radius)
		{
			radius = PluginConfig.ForceGrowInitialRadius?.Value ?? 3f;
			if (args != null)
			{
				for (int i = 2; i < args.Length; i++)
				{
					if (args[i].StartsWith("radius=", StringComparison.OrdinalIgnoreCase)
					    && TryCoordinate(args[i].Substring("radius=".Length), out float parsed))
					{
						radius = parsed;
						break;
					}
				}
			}

			radius = Mathf.Clamp(radius, 0.25f, MaximumRadius);
			return true;
		}

		private static bool TryCoordinate(string value, out float result)
		{
			return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
			       && !float.IsNaN(result)
			       && !float.IsInfinity(result);
		}

		private static void Notify(Terminal context, string message)
		{
			context?.AddString(message);
			if (Player.m_localPlayer)
				Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message, 0, null);
		}
	}
}
