using System;
using System.Collections.Generic;
using SonicWorldTools.Config;
using SonicWorldTools.Integration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SonicWorldTools.Modules.PrecisionAreaPipette
{
	/// <summary>
	/// Locked-selection dismantle (refunds) / permanent delete (no drops).
	/// Mirrors Dismantleheim live paths; records WEC/IH undo when available.
	/// </summary>
	internal static class SelectionRemoval
	{
		internal enum RemovalKind
		{
			DismantleRefunds = 0,
			PermanentDelete = 1,
		}

		internal static bool TryRemoveEffectiveSelection(
			IReadOnlyCollection<string> keys,
			IReadOnlyDictionary<string, GameObject> keyToRoot,
			RemovalKind kind,
			out int requested,
			out int skipped,
			out string summary)
		{
			requested = 0;
			skipped = 0;
			summary = string.Empty;

			if (keys == null || keys.Count == 0)
			{
				summary = "nothing selected";
				return false;
			}

			bool dry = PluginConfig.IsDryRunSelection();
			bool undo = WorldEditUndoBridge.IsAvailable && !dry;
			if (undo)
			{
				WorldEditUndoBridge.BeginAction();
			}

			try
			{
				foreach (string key in keys)
				{
					if (string.IsNullOrEmpty(key))
					{
						skipped++;
						continue;
					}

					GameObject root = null;
					if (keyToRoot != null)
					{
						keyToRoot.TryGetValue(key, out root);
					}

					if ((Object)(object)root == (Object)null)
					{
						TryResolveRoot(key, out root);
					}

					if ((Object)(object)root == (Object)null)
					{
						skipped++;
						LogVerbose("Removal skip missing root: " + key);
						continue;
					}

					ZNetView view = root.GetComponent<ZNetView>();
					ZDO zdo = (Object)(object)view != (Object)null && view.IsValid()
						? view.GetZDO()
						: null;

					if (dry)
					{
						requested++;
						LogVerbose(
							"DryRun would "
							+ (kind == RemovalKind.DismantleRefunds ? "dismantle" : "delete")
							+ ": " + root.name);
						continue;
					}

					if (undo && zdo != null)
					{
						WorldEditUndoBridge.AddRemoveAction(zdo);
					}

					bool ok = kind == RemovalKind.DismantleRefunds
						? TryDismantle(root, out string err)
						: TryPermanentDelete(root, out err);

					if (ok)
					{
						requested++;
					}
					else
					{
						skipped++;
						// Always log skip reasons — "skipped N" alone is not actionable.
						SonicWorldToolsPlugin.ModLogger?.LogInfo(
							"[SonicWorldTools] Removal skip " + root.name + ": " + err);
					}
				}
			}
			finally
			{
				if (undo)
				{
					WorldEditUndoBridge.EndAction();
				}
			}

			string verb = kind == RemovalKind.DismantleRefunds ? "Dismantle" : "Delete";
			summary = dry
				? "Dry-run " + verb.ToLowerInvariant() + " " + requested + " (skipped " + skipped + ")"
				: verb + " requested " + requested + " (skipped " + skipped + ")"
				  + (undo ? "; undo available" : string.Empty);
			return requested > 0;
		}

		private static bool TryDismantle(GameObject go, out string error)
		{
			error = string.Empty;
			Piece piece = go.GetComponent<Piece>();
			if ((Object)(object)piece == (Object)null)
			{
				error = "no Piece";
				return false;
			}

			Player player = Player.m_localPlayer;
			if ((Object)(object)player == (Object)null)
			{
				error = "no player";
				return false;
			}

			Vector3 pos = go.transform.position;
			if (!PrivateArea.CheckAccess(pos, 0f, flash: true, wardCheck: false))
			{
				error = "ward denied";
				return false;
			}

			// No crafting-station range gate: pipette Ctrl+Del is mass edit with refunds.
			// Vanilla CheckCanRemovePiece / $msg_missingstation does not apply here
			// (Shift+Del already skips it; station range made Ctrl+Del unusable under
			// nocost / nearby-crafting style play).

			if (!piece.m_canBeRemoved || !piece.CanBeRemoved())
			{
				error = "Piece.CanBeRemoved=false";
				return false;
			}

			IRemoved removedHook = go.GetComponent<IRemoved>();
			if (removedHook != null)
			{
				removedHook.OnRemoved();
			}

			WearNTear wear = go.GetComponent<WearNTear>();
			if ((Object)(object)wear == (Object)null)
			{
				error = "no WearNTear";
				return false;
			}

			// blockDrop=false → refunds via WearNTear destroy path (same as Dismantleheim).
			wear.Remove(false);
			return true;
		}

		private static bool TryPermanentDelete(GameObject go, out string error)
		{
			error = string.Empty;
			Vector3 pos = go.transform.position;
			if (!PrivateArea.CheckAccess(pos, 0f, flash: true, wardCheck: false))
			{
				error = "ward denied";
				return false;
			}

			if ((Object)(object)go.GetComponent<TeleportWorld>() != (Object)null)
			{
				error = "portal";
				return false;
			}

			if ((Object)(object)go.GetComponent<PrivateArea>() != (Object)null)
			{
				error = "ward object";
				return false;
			}

			Piece piece = go.GetComponent<Piece>();
			if ((Object)(object)piece != (Object)null && !piece.m_canBeRemoved)
			{
				error = "m_canBeRemoved=false";
				return false;
			}

			WearNTear wear = go.GetComponent<WearNTear>();
			if ((Object)(object)wear == (Object)null)
			{
				wear = go.GetComponentInChildren<WearNTear>();
			}

			if ((Object)(object)wear != (Object)null)
			{
				// blockDrop=true → no DropResources.
				wear.Remove(true);
				return true;
			}

			error = "no WearNTear";
			return false;
		}

		private static bool TryResolveRoot(string key, out GameObject root)
		{
			root = null;
			ZNetScene scene = ZNetScene.instance;
			if ((Object)(object)scene == (Object)null)
			{
				return false;
			}

			if (!InfinityHammerBridge.TryParseZdoid(key, out ZDOID zdoid) || zdoid.IsNone())
			{
				return false;
			}

			root = scene.FindInstance(zdoid);
			return (Object)(object)root != (Object)null;
		}

		private static void LogVerbose(string message)
		{
			if (PluginConfig.IsVerboseLogging())
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[SonicWorldTools] " + message);
			}
		}
	}
}
