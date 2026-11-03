using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Explicitly exports loaded building-piece geometry for YAML calibration.</summary>
	internal static class PrefabGeometryDumpCommand
	{
		private const string CommandName = "sonic_piece_geometry_dump";
		private static bool _registered;

		internal static void Register()
		{
			if (_registered) return;
			new Terminal.ConsoleCommand(CommandName, "Export geometry and snap data for all loaded building pieces to a TSV diagnostics file.", args =>
			{
				if (args.Length > 1)
				{
					args.Context.AddString("Usage: " + CommandName);
					return;
				}
				Export(args.Context);
			});
			_registered = true;
		}

		private static void Export(Terminal context)
		{
			ZNetScene scene = ZNetScene.instance;
			if (!scene || scene.m_prefabs == null)
			{
				context.AddString("Piece geometry dump: enter a world first so prefabs are loaded.");
				return;
			}

			var output = new StringBuilder(128 * 1024);
			output.AppendLine("PrefabId\tPieceName\tProfileType\tStatus\tMeasurementSource\tConfidence\tRepeatAxis\tResolvedSpacing\tJoinStart\tJoinEnd\tCenterOffset\tYawOffset\tBottomOffset\tHasRenderers\tRenderCenter\tRenderSize\tRenderMin\tRenderMax\tSnapMin\tSnapMax\tSnapPoints");
			int exported = 0;
			int failed = 0;
			foreach (KeyValuePair<string, GameObject> candidate in PrefabPlacementMetadata.GetBuildTableCandidates())
			{
				GameObject prefab = candidate.Value;
				if (!prefab) continue;
				Piece piece = prefab.GetComponent<Piece>();
				if (!piece) continue;
				try
				{
					ResolvedPrefabPlacement metadata = PrefabPlacementMetadata.Resolve(prefab.name, prefab);
					Bounds renderBounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
					Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
					var snaps = new List<Transform>();
					piece.GetSnapPoints(snaps);
					bool hasSnap = false;
					Bounds snapBounds = new Bounds(Vector3.zero, Vector3.zero);
					var snapText = new StringBuilder();
					for (int snapIndex = 0; snapIndex < snaps.Count; snapIndex++)
					{
						Transform snap = snaps[snapIndex];
						if (!snap) continue;
						Vector3 local = prefab.transform.InverseTransformPoint(snap.position);
						if (hasSnap) snapBounds.Encapsulate(local);
						else
						{
							snapBounds = new Bounds(local, Vector3.zero);
							hasSnap = true;
						}
						if (snapText.Length > 0) snapText.Append('|');
						snapText.Append(Clean(snap.name)).Append('@').Append(Vector(local));
					}

					output.Append(Clean(prefab.name)).Append('\t')
						.Append(Clean(piece.m_name)).Append('\t')
						.Append(metadata.ProfileType).Append('\t')
						.Append(metadata.Status).Append('\t')
						.Append(Clean(metadata.MeasurementSource)).Append('\t')
						.Append(Clean(metadata.Confidence)).Append('\t')
						.Append(metadata.Axis).Append('\t')
						.Append(Number(metadata.Spacing)).Append('\t')
						.Append(Vector(metadata.JoinStart)).Append('\t')
						.Append(Vector(metadata.JoinEnd)).Append('\t')
						.Append(Vector(metadata.CenterOffset)).Append('\t')
						.Append(Number(metadata.YawOffset)).Append('\t')
						.Append(Number(metadata.BottomOffset)).Append('\t')
						.Append(renderers.Length > 0 ? "true" : "false").Append('\t')
						.Append(Vector(renderBounds.center)).Append('\t')
						.Append(Vector(renderBounds.size)).Append('\t')
						.Append(Vector(renderBounds.min)).Append('\t')
						.Append(Vector(renderBounds.max)).Append('\t')
						.Append(hasSnap ? Vector(snapBounds.min) : string.Empty).Append('\t')
						.Append(hasSnap ? Vector(snapBounds.max) : string.Empty).Append('\t')
						.AppendLine(snapText.ToString());
					exported++;
				}
				catch (Exception ex)
				{
					failed++;
					SonicWorldToolsPlugin.ModLogger?.LogWarning("[PieceRepeater] Geometry dump skipped " + prefab.name + ": " + ex.Message);
				}
			}

			string directory = Path.Combine(Paths.ConfigPath, "SonicWorldTools", "PrefabPlacement", "diagnostics");
			Directory.CreateDirectory(directory);
			string filename = "piece-geometry-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".tsv";
			string path = Path.Combine(directory, filename);
			File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
			string message = "Piece geometry dump wrote " + exported + " build-table pieces" + (failed > 0 ? " (" + failed + " skipped)" : string.Empty) + ": " + path;
			context.AddString(message);
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[PieceRepeater] " + message);
		}

		private static string Number(float value)
		{
			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}

		private static string Vector(Vector3 value)
		{
			return Number(value.x) + "," + Number(value.y) + "," + Number(value.z);
		}

		private static string Clean(string value)
		{
			return (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
		}
	}
}
