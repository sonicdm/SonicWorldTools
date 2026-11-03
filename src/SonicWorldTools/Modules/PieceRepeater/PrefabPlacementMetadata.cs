using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Linq;
using System.Collections;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using SonicWorldTools.Config;

namespace SonicWorldTools.Modules.PieceRepeater
{
	internal enum PrefabRepeatAxis
	{
		Auto,
		X,
		Z,
		Vertical
	}
	internal enum RepeatProfileType { EdgeChain, GroundProp }
	internal enum RepeatProfileStatus { Ready, NeedsReview }

	internal struct ResolvedPrefabPlacement
	{
		internal PrefabRepeatAxis Axis;
		internal float Spacing;
		internal Vector3 CenterOffset;
		internal float YawOffset;
		internal float TangentYawOffset;
		internal float BottomOffset;
		internal bool HasBottomOffsetOverride;
		internal bool SupportsX;
		internal bool SupportsZ;
		internal RepeatProfileType ProfileType;
		internal RepeatProfileStatus Status;
		internal Vector3 JoinStart;
		internal Vector3 JoinEnd;
		internal string MeasurementSource;
		internal string Confidence;

		internal ResolvedPrefabPlacement(PrefabRepeatAxis axis, float spacing, Vector3 centerOffset, float yawOffset, float bottomOffset, bool hasBottomOffsetOverride, bool supportsX, bool supportsZ)
		{
			Axis = axis;
			Spacing = spacing;
			CenterOffset = centerOffset;
			YawOffset = yawOffset;
			TangentYawOffset = 0f;
			BottomOffset = bottomOffset;
			HasBottomOffsetOverride = hasBottomOffsetOverride;
			SupportsX = supportsX;
			SupportsZ = supportsZ;
			ProfileType = RepeatProfileType.EdgeChain;
			Status = RepeatProfileStatus.NeedsReview;
			JoinStart = Vector3.zero;
			JoinEnd = Vector3.zero;
			MeasurementSource = "legacy-fallback";
			Confidence = "Low";
		}
	}

	/// <summary>Loads editable placement profiles and generates a catalog from loaded build tables.</summary>
	internal static class PrefabPlacementMetadata
	{
		private const float MinimumSpacing = 0.25f;
		private const string CatalogName = "PieceProfiles.yml";
		private static Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
		private static ManualLogSource _log;
		private static FileSystemWatcher _watcher;
		private static System.Threading.Timer _reloadTimer;
		private static string _configDirectory;
		private static readonly object ReloadTimerLock = new object();
		private static readonly Dictionary<int, Bounds> RendererBoundsCache = new Dictionary<int, Bounds>();
		private static bool _catalogInitialized;
		private static bool _refreshCommandRegistered;
		private static float _nextCatalogAttempt;

		internal static void EnsureCatalog()
		{
			if (_catalogInitialized || Time.unscaledTime < _nextCatalogAttempt || !ZNetScene.instance || !Player.m_localPlayer) return;
			try
			{
				int found = RefreshCatalog();
				if (found > 0) _catalogInitialized = true;
				else _nextCatalogAttempt = Time.unscaledTime + 5f;
			}
			catch (Exception ex)
			{
				_nextCatalogAttempt = Time.unscaledTime + 10f;
				_log?.LogWarning("[PieceRepeater] Catalog generation failed: " + ex);
			}
		}

		internal static void RegisterRefreshCommand()
		{
			if (_refreshCommandRegistered) return;
			new Terminal.ConsoleCommand("sonic_piece_catalog_refresh", "Append newly loaded build pieces to the editable Piece Repeater catalog.", args =>
			{
				try
				{
					int scanned = RefreshCatalog();
					args.Context.AddString("Piece Repeater catalog: scanned " + scanned + " build pieces; see the log for appended profile count.");
				}
				catch (Exception ex) { args.Context.AddString("Piece Repeater catalog refresh failed: " + ex.Message); }
			});
			_refreshCommandRegistered = true;
		}

		private static int RefreshCatalog()
		{
			if (!ZNetScene.instance) return 0;
			RendererBoundsCache.Clear();
			string path = Path.Combine(_configDirectory, CatalogName);
			var existing = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
			if (File.Exists(path)) Parse(File.ReadAllLines(path), existing);
			var candidates = GetBuildTableCandidates();
			if (candidates.Count == 0) return 0;
			var yaml = new StringBuilder();
			int added = 0;
			foreach (KeyValuePair<string, GameObject> candidate in candidates)
			{
				if (existing.ContainsKey(candidate.Key)) continue;
				AppendGeneratedProfile(yaml, candidate.Key, candidate.Value);
				added++;
			}
			if (!File.Exists(path))
			{
				Directory.CreateDirectory(_configDirectory);
				File.WriteAllText(path, "# Generated from build-piece tables. Edit profiles here; refresh only appends new pieces.\n" + yaml, new System.Text.UTF8Encoding(false));
			}
			else if (added > 0) File.AppendAllText(path, yaml.ToString(), new System.Text.UTF8Encoding(false));
			if (added > 0) Reload(_log);
			_log?.LogInfo("[PieceRepeater] Catalog scanned " + candidates.Count + " build pieces; appended " + added + " profiles at " + path);
			return candidates.Count;
		}

		internal static SortedDictionary<string, GameObject> GetBuildTableCandidates()
		{
			var candidates = new SortedDictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
			var tables = new HashSet<PieceTable>();
			if (ObjectDB.instance && ObjectDB.instance.m_items != null)
			{
				foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
				{
					ItemDrop item = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
					object shared = item != null ? item.m_itemData?.m_shared : null;
					if (shared == null) continue;
					PieceTable table = HarmonyLib.AccessTools.Field(shared.GetType(), "m_buildPieces")?.GetValue(shared) as PieceTable;
					if (table) tables.Add(table);
				}
			}
			if (Player.m_localPlayer)
			{
				PieceTable current = HarmonyLib.AccessTools.Field(typeof(Player), "m_buildPieces")?.GetValue(Player.m_localPlayer) as PieceTable;
				if (current) tables.Add(current);
			}
			var piecesField = HarmonyLib.AccessTools.Field(typeof(PieceTable), "m_pieces");
			foreach (PieceTable table in tables)
			{
				if (!table) continue;
				if (!(piecesField?.GetValue(table) is IEnumerable pieces)) continue;
				foreach (object item in pieces)
				{
					GameObject prefab = item as GameObject;
					if (!prefab || !prefab.GetComponent<Piece>() || IsExcludedPiece(prefab)) continue;
					if (!candidates.ContainsKey(prefab.name)) candidates.Add(prefab.name, prefab);
				}
			}
			return candidates;
		}

		internal static bool IsExcludedPiece(GameObject prefab)
		{
			if (!prefab) return true;
			foreach (Component component in prefab.GetComponents<Component>())
			{
				if (!component) continue;
				string name = component.GetType().Name;
				if (name.EndsWith("Station", StringComparison.OrdinalIgnoreCase)
				    || string.Equals(name, "Fermenter", StringComparison.OrdinalIgnoreCase)
				    || string.Equals(name, "Smelter", StringComparison.OrdinalIgnoreCase)) return true;
			}
			return false;
		}

		private static void AppendGeneratedProfile(StringBuilder yaml, string id, GameObject prefab)
		{
			Bounds bounds = GetLocalRendererBounds(prefab);
			Piece piece = prefab.GetComponent<Piece>();
			var snaps = new List<Transform>();
			piece.GetSnapPoints(snaps);
			Bounds snapBounds = new Bounds(Vector3.zero, Vector3.zero);
			bool hasSnap = false;
			foreach (Transform snap in snaps)
			{
				if (!snap) continue;
				Vector3 local = prefab.transform.InverseTransformPoint(snap.position);
				local.y = 0f;
				if (hasSnap) snapBounds.Encapsulate(local);
				else { snapBounds = new Bounds(local, Vector3.zero); hasSnap = true; }
			}
			float x = hasSnap ? snapBounds.size.x : bounds.size.x;
			float z = hasSnap ? snapBounds.size.z : bounds.size.z;
			bool axisX = x > 0.5f && x > z * 1.4f;
			bool axisZ = z > 0.5f && z > x * 1.4f;
			if (!axisX && !axisZ)
			{
				axisX = bounds.size.x > 0.5f && bounds.size.x > bounds.size.z * 1.8f;
				axisZ = bounds.size.z > 0.5f && bounds.size.z > bounds.size.x * 1.8f;
			}
			bool floorPanel = id.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0
				&& Mathf.Min(x, z) >= 0.5f;
			// Square floor panels have four valid joins, so use a consistent local Z
			// edge as the repeat axis for path runs. Other square pieces remain review-only.
			if (floorPanel && !axisX && !axisZ)
			{
				axisZ = true;
				x = hasSnap ? snapBounds.size.x : bounds.size.x;
				z = hasSnap ? snapBounds.size.z : bounds.size.z;
			}
			// A narrow standing prop can have a long arm, but its horizontal edges
			// are not repeat joints. Do not classify those arms as fence segments.
			float horizontalExtent = Mathf.Max(bounds.size.x, bounds.size.z);
			bool namedColumn = (id.IndexOf("pillar", StringComparison.OrdinalIgnoreCase) >= 0
					|| id.IndexOf("column", StringComparison.OrdinalIgnoreCase) >= 0)
				&& id.IndexOf("arch", StringComparison.OrdinalIgnoreCase) < 0
				&& id.IndexOf("beam", StringComparison.OrdinalIgnoreCase) < 0
				&& id.IndexOf("base", StringComparison.OrdinalIgnoreCase) < 0;
			bool standingProp = (!hasSnap || namedColumn) && horizontalExtent < 1.5f
				&& bounds.size.y > Mathf.Max(1.5f, horizontalExtent * 1.5f);
			if (standingProp && !floorPanel) axisX = axisZ = false;
			bool chain = axisX || axisZ;
			bool centered = Mathf.Abs(bounds.center.x) <= Mathf.Max(0.35f, bounds.size.x * 0.35f)
				&& Mathf.Abs(bounds.center.z) <= Mathf.Max(0.35f, bounds.size.z * 0.35f);
			bool grounded = bounds.min.y > -5f && bounds.max.y < 5f;
			bool snapEdgesConsistent = !hasSnap || !chain || (axisX
				? Mathf.Abs(snapBounds.min.x - bounds.min.x) < 0.45f && Mathf.Abs(snapBounds.max.x - bounds.max.x) < 0.45f
				: Mathf.Abs(snapBounds.min.z - bounds.min.z) < 0.45f && Mathf.Abs(snapBounds.max.z - bounds.max.z) < 0.45f);
			bool likelyWallMount = id.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("sconce", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0;
			bool ready = chain ? Mathf.Max(bounds.size.x, bounds.size.z) > 0.5f && bounds.size.y < 12f && snapEdgesConsistent
				: (!hasSnap || standingProp) && (centered || standingProp) && grounded && !likelyWallMount && horizontalExtent < 1.5f;
			string source = chain && hasSnap ? "ProjectedSnapEdges" : chain ? "IntactMeshEdges" : "GroundFootprint";
			float start = axisX ? (hasSnap ? snapBounds.min.x : bounds.min.x) : (hasSnap ? snapBounds.min.z : bounds.min.z);
			float end = axisX ? (hasSnap ? snapBounds.max.x : bounds.max.x) : (hasSnap ? snapBounds.max.z : bounds.max.z);
			float transverse = axisX ? bounds.center.z : bounds.center.x;
			Vector3 joinStart = axisX ? new Vector3(start, 0f, transverse) : new Vector3(transverse, 0f, start);
			Vector3 joinEnd = axisX ? new Vector3(end, 0f, transverse) : new Vector3(transverse, 0f, end);
			yaml.Append('\n').Append('"').Append(id.Replace("\"", "")).AppendLine("\":");
			yaml.Append("  ProfileType: ").AppendLine(chain ? "EdgeChain" : "GroundProp");
			yaml.Append("  Status: ").AppendLine(ready ? "Ready" : "NeedsReview");
			yaml.Append("  MeasurementSource: ").AppendLine(ready ? source : "Uncertain");
			yaml.Append("  Confidence: ").AppendLine(ready ? "High" : "Low");
			yaml.Append("  ShapeAxis: ").AppendLine(axisX ? "X" : axisZ ? "Z" : "Auto");
			if (chain)
			{
				yaml.Append("  JoinStart: ").AppendLine(FormatVector(joinStart));
				yaml.Append("  JoinEnd: ").AppendLine(FormatVector(joinEnd));
			}
			else
			{
				yaml.AppendLine("  # DefaultSpacing: 2  # Uncomment to override the shared setting.");
				yaml.AppendLine("  TangentYawOffset: 0");
			}
			yaml.Append("  ShapeBottomOffset: ").AppendLine(bounds.min.y.ToString("0.###", CultureInfo.InvariantCulture));
		}

		private static string FormatVector(Vector3 value) => value.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + value.y.ToString("0.###", CultureInfo.InvariantCulture) + "," + value.z.ToString("0.###", CultureInfo.InvariantCulture);

		private sealed class Entry
		{
			internal readonly HashSet<string> Directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			internal PrefabRepeatAxis Axis = PrefabRepeatAxis.Auto;
			internal float DimensionX;
			internal float DimensionY;
			internal float DimensionZ;
			internal float DimensionBottomOffset;
			internal bool HasDimensionBottomOffset;
			internal float ShapeSpacing;
			internal bool HasShapeSpacing;
			internal Vector3 ShapeCenterOffset;
			internal bool HasShapeCenterOffset;
			internal bool UseSnapCenter;
			internal float ShapeYawOffset;
			internal float TangentYawOffset;
			internal float ShapeBottomOffset;
			internal bool HasShapeBottomOffset;
			internal RepeatProfileType ProfileType;
			internal bool HasProfileType;
			internal RepeatProfileStatus Status = RepeatProfileStatus.NeedsReview;
			internal Vector3 JoinStart;
			internal Vector3 JoinEnd;
			internal bool HasJoinStart;
			internal bool HasJoinEnd;
			internal float DefaultSpacing;
			internal bool HasDefaultSpacing;
			internal string MeasurementSource = "legacy-fallback";
			internal string Confidence = "Low";
		}

		internal static void Load(string configRoot, ManualLogSource log, ConfigEntry<bool> droppedDefaultYaml, ConfigFile config)
		{
			_log = log;
			_configDirectory = Path.Combine(configRoot ?? string.Empty, "SonicWorldTools", "PrefabPlacement");
			try
			{
				Directory.CreateDirectory(_configDirectory);
				Reload(log);
				StartWatcher();
			}

			catch (Exception ex)
			{
				log?.LogWarning("Could not initialize prefab placement metadata in " + _configDirectory + ": " + ex.Message + ". Using prefab geometry fallback.");
			}
		}

		internal static void Shutdown()
		{
			RendererBoundsCache.Clear();
			if (_watcher != null)
			{
				_watcher.EnableRaisingEvents = false;
				_watcher.Dispose();
				_watcher = null;
			}
			lock (ReloadTimerLock)
			{
				if (_reloadTimer != null)
				{
					_reloadTimer.Dispose();
					_reloadTimer = null;
				}
			}
		}

		private static void StartWatcher()
		{
			Shutdown();
			_watcher = new FileSystemWatcher(_configDirectory, "*.y*ml")
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
				EnableRaisingEvents = true
			};
			_watcher.Changed += OnConfigChanged;
			_watcher.Created += OnConfigChanged;
			_watcher.Deleted += OnConfigChanged;
			_watcher.Renamed += OnConfigChanged;
		}

		private static void OnConfigChanged(object sender, FileSystemEventArgs args)
		{
			lock (ReloadTimerLock)
			{
				if (_reloadTimer == null)
					_reloadTimer = new System.Threading.Timer(_ => Reload(_log), null, 350, Timeout.Infinite);
				else
					_reloadTimer.Change(350, Timeout.Infinite);
			}
		}

		private static void Reload(ManualLogSource log)
		{
			try
			{
				var parsed = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
				foreach (string file in Directory.GetFiles(_configDirectory, "*.yml", SearchOption.AllDirectories).OrderBy(f => Path.GetFileName(f) == CatalogName ? 0 : 1))
					Parse(File.ReadAllLines(file), parsed);
				foreach (string file in Directory.GetFiles(_configDirectory, "*.yaml", SearchOption.AllDirectories).OrderBy(f => Path.GetFileName(f) == CatalogName ? 0 : 1))
					Parse(File.ReadAllLines(file), parsed);
				Interlocked.Exchange(ref Entries, parsed);
				log?.LogInfo("Loaded " + parsed.Count + " prefab placement metadata entries from " + _configDirectory);
			}
			catch (Exception ex)
			{
				log?.LogWarning("Could not reload prefab placement metadata from " + _configDirectory + ": " + ex.Message + ". Keeping the last valid configuration.");
			}
		}

		internal static ResolvedPrefabPlacement Resolve(string prefabId, GameObject prefab, PrefabRepeatAxis axisOverride = PrefabRepeatAxis.Auto)
		{
			Dictionary<string, Entry> entries = Interlocked.CompareExchange(ref Entries, null, null);
			entries.TryGetValue(prefabId ?? string.Empty, out Entry entry);
			Bounds geometryBounds = GetLocalRendererBounds(prefab);
			bool hasSnapBounds = TryGetLocalSnapBounds(prefab, out Bounds snapBounds);
			Bounds dimensions = hasSnapBounds ? snapBounds : geometryBounds;
			PrefabRepeatAxis axis = axisOverride == PrefabRepeatAxis.Auto ? ResolveAxis(entry, dimensions) : axisOverride;
			float spacing = ResolveSpacing(entry, axis, dimensions, geometryBounds);
			bool hasJoins = entry != null && entry.HasJoinStart && entry.HasJoinEnd;
			if (entry != null && entry.HasProfileType && entry.ProfileType == RepeatProfileType.GroundProp)
				spacing = entry.HasDefaultSpacing ? entry.DefaultSpacing : PluginConfig.PieceRepeaterGroundPropSpacing?.Value ?? 2f;
			else if (hasJoins)
			{
				Vector3 delta = entry.JoinEnd - entry.JoinStart;
				delta.y = 0f;
				spacing = delta.magnitude;
			}
			bool supportsX = entry != null
				? entry.Directions.Contains("LeftRight") || entry.DimensionX > 0f
				: dimensions.size.x > MinimumSpacing;
			bool supportsZ = entry != null
				? entry.Directions.Contains("ForwardBackward") || entry.DimensionZ > 0f
				: dimensions.size.z > MinimumSpacing;
			Vector3 centerOffset = entry != null && entry.HasProfileType && entry.ProfileType == RepeatProfileType.GroundProp
				&& !entry.HasShapeCenterOffset ? Vector3.zero : hasJoins
				? (entry.JoinStart + entry.JoinEnd) * 0.5f
				: entry != null && entry.HasShapeCenterOffset
				? entry.ShapeCenterOffset
				: entry != null && entry.UseSnapCenter && hasSnapBounds
					? new Vector3(snapBounds.center.x, 0f, snapBounds.center.z)
					: Vector3.zero;
			bool hasBottomOffsetOverride = entry != null && (entry.HasShapeBottomOffset || entry.HasDimensionBottomOffset);
			float bottomOffset = entry != null && entry.HasShapeBottomOffset
				? entry.ShapeBottomOffset
				: entry != null && entry.HasDimensionBottomOffset
					? entry.DimensionBottomOffset
					: hasSnapBounds ? snapBounds.min.y : geometryBounds.min.y;
			var result = new ResolvedPrefabPlacement(axis, Mathf.Max(MinimumSpacing, spacing), centerOffset, entry != null ? entry.ShapeYawOffset : 0f, bottomOffset, hasBottomOffsetOverride, supportsX, supportsZ);
			result.TangentYawOffset = entry != null ? entry.TangentYawOffset : 0f;
			result.ProfileType = entry != null ? entry.ProfileType : RepeatProfileType.EdgeChain;
			result.Status = entry != null ? entry.Status : RepeatProfileStatus.NeedsReview;
			if (result.ProfileType == RepeatProfileType.EdgeChain && (!hasJoins || spacing <= MinimumSpacing))
				result.Status = RepeatProfileStatus.NeedsReview;
			result.JoinStart = hasJoins ? entry.JoinStart : Vector3.zero;
			result.JoinEnd = hasJoins ? entry.JoinEnd : Vector3.zero;
			result.MeasurementSource = entry != null ? entry.MeasurementSource : "unprofiled";
			result.Confidence = entry != null ? entry.Confidence : "Low";
			return result;
		}

		private static PrefabRepeatAxis ResolveAxis(Entry entry, Bounds bounds)
		{
			if (entry == null) return InferAxis(bounds);
			if (entry.HasJoinStart && entry.HasJoinEnd)
			{
				Vector3 join = entry.JoinEnd - entry.JoinStart;
				return Mathf.Abs(join.x) >= Mathf.Abs(join.z) ? PrefabRepeatAxis.X : PrefabRepeatAxis.Z;
			}
			if (entry.Axis != PrefabRepeatAxis.Auto) return entry.Axis;

			bool leftRight = entry.Directions.Contains("LeftRight");
			bool forwardBackward = entry.Directions.Contains("ForwardBackward");
			bool upDown = entry.Directions.Contains("UpDown");
			if (leftRight && !forwardBackward) return PrefabRepeatAxis.X;
			if (forwardBackward && !leftRight) return PrefabRepeatAxis.Z;
			if (upDown && !leftRight && !forwardBackward) return PrefabRepeatAxis.Vertical;

			if (leftRight && forwardBackward)
			{
				if (entry.DimensionX > entry.DimensionZ) return PrefabRepeatAxis.X;
				if (entry.DimensionZ > entry.DimensionX) return PrefabRepeatAxis.Z;
			}
			else
			{
				if (entry.DimensionX > 0f && entry.DimensionX > entry.DimensionZ) return PrefabRepeatAxis.X;
				if (entry.DimensionZ > 0f && entry.DimensionZ > entry.DimensionX) return PrefabRepeatAxis.Z;
			}

			return InferAxis(bounds);
		}

		private static PrefabRepeatAxis InferAxis(Bounds bounds)
		{
			return bounds.size.x > bounds.size.z ? PrefabRepeatAxis.X : PrefabRepeatAxis.Z;
		}

		private static float ResolveSpacing(Entry entry, PrefabRepeatAxis axis, Bounds dimensions, Bounds geometry)
		{
			if (entry != null && entry.HasShapeSpacing && entry.ShapeSpacing > 0f)
				return entry.ShapeSpacing;
			// DragNBuild Dimensions are fallback hints, not physical measurements.
			// When a prefab exposes snap points, their local span is the actual
			// center-to-center repeat distance and must win over inherited YAML values.
			if (axis == PrefabRepeatAxis.X && dimensions.size.x > MinimumSpacing) return dimensions.size.x;
			if (axis == PrefabRepeatAxis.Z && dimensions.size.z > MinimumSpacing) return dimensions.size.z;
			if (axis == PrefabRepeatAxis.Vertical && Mathf.Max(dimensions.size.x, dimensions.size.z) > MinimumSpacing)
				return Mathf.Max(dimensions.size.x, dimensions.size.z);
			if (entry != null)
			{
				if (axis == PrefabRepeatAxis.X && entry.DimensionX > 0f) return entry.DimensionX;
				if (axis == PrefabRepeatAxis.Z && entry.DimensionZ > 0f) return entry.DimensionZ;
				if (axis == PrefabRepeatAxis.Vertical && Mathf.Max(entry.DimensionX, entry.DimensionZ) > 0f)
					return Mathf.Max(entry.DimensionX, entry.DimensionZ);
			}
			if (axis == PrefabRepeatAxis.X) return geometry.size.x;
			if (axis == PrefabRepeatAxis.Z) return geometry.size.z;
			return Mathf.Max(geometry.size.x, geometry.size.z);
		}

		private static bool TryGetLocalSnapBounds(GameObject prefab, out Bounds bounds)
		{
			bounds = new Bounds(Vector3.zero, Vector3.zero);
			if (!prefab) return false;
			Piece piece = prefab.GetComponent<Piece>();
			if (!piece) return false;
			var snapPoints = new List<Transform>();
			piece.GetSnapPoints(snapPoints);
			bool hasPoint = false;
			foreach (Transform snap in snapPoints)
			{
				if (!snap) continue;
				Vector3 local = prefab.transform.InverseTransformPoint(snap.position);
				if (!hasPoint)
				{
					bounds = new Bounds(local, Vector3.zero);
					hasPoint = true;
				}
				else bounds.Encapsulate(local);
			}
			return hasPoint;
		}

		internal static Bounds GetLocalRendererBounds(GameObject prefab)
		{
			if (!prefab) return new Bounds(Vector3.zero, Vector3.one * MinimumSpacing);
			int prefabKey = prefab.GetInstanceID();
			if (RendererBoundsCache.TryGetValue(prefabKey, out Bounds cached)) return cached;
			Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
			bool hasBounds = false;
			Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
			foreach (Renderer renderer in renderers)
			{
				if (!renderer || !renderer.enabled) continue;
				bool intact = true;
				for (Transform current = renderer.transform; current && current != prefab.transform; current = current.parent)
					if (!current.gameObject.activeSelf) { intact = false; break; }
				if (!intact) continue;
				Bounds local;
				if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh) local = skin.sharedMesh.bounds;
				else
				{
					MeshFilter filter = renderer.GetComponent<MeshFilter>();
					if (!filter || !filter.sharedMesh) continue;
					local = filter.sharedMesh.bounds;
				}
				for (int corner = 0; corner < 8; corner++)
				{
					Vector3 point = new Vector3(
						(corner & 1) == 0 ? local.min.x : local.max.x,
						(corner & 2) == 0 ? local.min.y : local.max.y,
						(corner & 4) == 0 ? local.min.z : local.max.z);
					point = prefab.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
					if (!hasBounds)
					{
						combined = new Bounds(point, Vector3.zero);
						hasBounds = true;
					}
					else combined.Encapsulate(point);
				}
			}
			Bounds result = hasBounds ? combined : new Bounds(Vector3.zero, Vector3.one * MinimumSpacing);
			RendererBoundsCache[prefabKey] = result;
			return result;
		}

		private static void Parse(string[] lines, Dictionary<string, Entry> entries)
		{
			Entry current = null;
			string currentId = null;
			bool inDimensions = false;
			foreach (string raw in lines)
			{
				if (string.IsNullOrWhiteSpace(raw)) continue;
				int indent = 0;
				while (indent < raw.Length && raw[indent] == ' ') indent++;
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

				if (indent == 0 && line.EndsWith(":", StringComparison.Ordinal))
				{
					currentId = line.Substring(0, line.Length - 1).Trim().Trim('"', '\'');
					if (!entries.TryGetValue(currentId, out current)) current = new Entry();
					entries[currentId] = current;
					inDimensions = false;
					continue;
				}
				if (current == null) continue;

				if (indent == 2)
				{
					inDimensions = string.Equals(line, "Dimensions:", StringComparison.OrdinalIgnoreCase);
					if (inDimensions) continue;
					if (!TryReadPair(line, out string key, out string value)) continue;
					if (string.Equals(key, "Directions", StringComparison.OrdinalIgnoreCase))
					{
						foreach (string direction in value.Split(','))
							current.Directions.Add(direction.Trim());
					}
					else if (string.Equals(key, "ShapeAxis", StringComparison.OrdinalIgnoreCase))
					{
						if (Enum.TryParse(value, true, out PrefabRepeatAxis axis)) current.Axis = axis;
					}
					else if (string.Equals(key, "ShapeSpacing", StringComparison.OrdinalIgnoreCase))
					{
						current.HasShapeSpacing = TryParseFloat(value, out current.ShapeSpacing);
					}
					else if (string.Equals(key, "ShapeCenterOffset", StringComparison.OrdinalIgnoreCase))
					{
						current.HasShapeCenterOffset = TryParseVector3(value, out current.ShapeCenterOffset);
					}
					else if (string.Equals(key, "ShapeYawOffset", StringComparison.OrdinalIgnoreCase))
					{
						if (TryParseFloat(value, out float yawOffset)) current.ShapeYawOffset = yawOffset;
					}
					else if (string.Equals(key, "TangentYawOffset", StringComparison.OrdinalIgnoreCase))
					{
						if (TryParseFloat(value, out float tangentYawOffset)) current.TangentYawOffset = tangentYawOffset;
					}
					else if (string.Equals(key, "ShapeBottomOffset", StringComparison.OrdinalIgnoreCase))
					{
						current.HasShapeBottomOffset = TryParseFloat(value, out current.ShapeBottomOffset);
					}
					else if (string.Equals(key, "ShapeAnchor", StringComparison.OrdinalIgnoreCase))
					{
						current.UseSnapCenter = string.Equals(value, "SnapCenter", StringComparison.OrdinalIgnoreCase);
					}
					else if (string.Equals(key, "ProfileType", StringComparison.OrdinalIgnoreCase))
						current.HasProfileType = Enum.TryParse(value, true, out current.ProfileType);
					else if (string.Equals(key, "Status", StringComparison.OrdinalIgnoreCase))
						Enum.TryParse(value, true, out current.Status);
					else if (string.Equals(key, "JoinStart", StringComparison.OrdinalIgnoreCase))
						current.HasJoinStart = TryParseVector3(value, out current.JoinStart);
					else if (string.Equals(key, "JoinEnd", StringComparison.OrdinalIgnoreCase))
						current.HasJoinEnd = TryParseVector3(value, out current.JoinEnd);
					else if (string.Equals(key, "DefaultSpacing", StringComparison.OrdinalIgnoreCase))
						current.HasDefaultSpacing = TryParseFloat(value, out current.DefaultSpacing);
					else if (string.Equals(key, "MeasurementSource", StringComparison.OrdinalIgnoreCase))
						current.MeasurementSource = value;
					else if (string.Equals(key, "Confidence", StringComparison.OrdinalIgnoreCase))
						current.Confidence = value;
					continue;
				}

				if (indent == 4 && inDimensions && TryReadPair(line, out string dimension, out string dimensionValue)
			    && TryParseFloat(dimensionValue, out float numericDimension))
				{
					if (string.Equals(dimension, "X", StringComparison.OrdinalIgnoreCase)) current.DimensionX = numericDimension;
					else if (string.Equals(dimension, "Y", StringComparison.OrdinalIgnoreCase)) current.DimensionY = numericDimension;
					else if (string.Equals(dimension, "Z", StringComparison.OrdinalIgnoreCase)) current.DimensionZ = numericDimension;
					else if (string.Equals(dimension, "BottomOffset", StringComparison.OrdinalIgnoreCase))
					{
						// DragNBuild negates the YAML BottomOffset when building its dimensions value.
						current.DimensionBottomOffset = -numericDimension;
						current.HasDimensionBottomOffset = true;
					}
				}
			}
		}

		private static bool TryReadPair(string line, out string key, out string value)
		{
			int separator = line.IndexOf(':');
			if (separator <= 0)
			{
				key = value = null;
				return false;
			}
			key = line.Substring(0, separator).Trim();
			value = line.Substring(separator + 1).Trim().Trim('"', '\'');
			return true;
		}

		private static bool TryParseFloat(string text, out float value)
		{
			return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
			       && !float.IsNaN(value)
			       && !float.IsInfinity(value);
		}

		private static bool TryParseVector3(string text, out Vector3 value)
		{
			value = Vector3.zero;
			string[] parts = text.Split(',');
			if (parts.Length != 3
			    || !TryParseFloat(parts[0].Trim(), out float x)
			    || !TryParseFloat(parts[1].Trim(), out float y)
			    || !TryParseFloat(parts[2].Trim(), out float z)) return false;
			value = new Vector3(x, y, z);
			return true;
		}
	}
}
