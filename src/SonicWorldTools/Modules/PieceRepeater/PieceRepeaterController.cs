using System;
using System.Collections.Generic;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using SonicWorldTools.Integration;
using SonicWorldTools.Modules.PrecisionAreaPipette;
using UnityEngine;
using Jotunn.Configs;
using Jotunn.Managers;
using BepInEx.Configuration;

namespace SonicWorldTools.Modules.PieceRepeater
{
	internal enum PieceRepeaterMode
	{
		Path,
		Circle,
		Square,
		Rectangle
	}

	/// <summary>IH-opened prefab line tool with metadata-driven repeat-axis selection.</summary>
	internal sealed class PieceRepeaterController
	{
		private const int MaximumPlacements = 512;
		private const int MaximumPreviewDots = 192;
		private const float MinimumSpacing = 0.25f;
		private const float RotationStepDegrees = 22.5f;
		private static PieceRepeaterController _instance;

		private readonly List<Vector3> _points = new List<Vector3>();
		private readonly List<Placement> _placements = new List<Placement>();
		private readonly List<Vector3> _outline = new List<Vector3>();
		private readonly RaycastHit[] _surfaceRayHits = new RaycastHit[64];
		private string _prefabId;
		private PieceRepeaterMode _mode;
		private bool _filled;
		private bool _bothSides;
		private bool _hasLockedStartPlacement;
		private Placement _lockedStartPlacement;
		private int _snapPointIndex = -1;
		private bool _active;
		private KeyHintConfig _keyHints;
		private bool _awaitPrimaryRelease;
		private float _gapMeters;
		private int _rotationSteps;
		private int _rotationStepsAtStart;
		private float _sideOffset = 2f;
		private Vector3 _aim;
		private float _sampledYaw;
		private IDisposable _restoreIhShapeShortcut;
		private bool _hasAim;
		private GameObject _previewRoot;
		private LineRenderer _outlineLine;
		private LineRenderer _secondLine;
		private Material _previewMaterial;
		private Material _pieceGhostMaterial;
		private string _pieceGhostPrefabId;
		private readonly List<GameObject> _pieceGhosts = new List<GameObject>();
		private GameObject _snapProbeContainer;
		private GameObject _snapProbe;
		private string _snapProbePrefabId;
		private string _lastPathDiagnosticKey;
		private int _lastPathDiagnosticPointCount = -1;
		private Vector3 _lastPathDiagnosticAim;
		private float _lastPathCenterDeviation;
		private readonly PieceRepeaterSettingsPanel _settingsPanel;

		private struct Placement
		{
			internal Vector3 Position;
			internal Quaternion Rotation;

			internal Placement(Vector3 position, Quaternion rotation)
			{
				Position = position;
				Rotation = rotation;
			}
		}

		internal static bool IsActive => _instance != null && _instance._active;
		internal static bool OwnsPlayerInput => IsActive && Player.m_localPlayer && Player.m_localPlayer.InPlaceMode();
		internal static PieceRepeaterController Instance => _instance;
		internal static bool IsPlacingBatch { get; private set; }
		internal static bool OwnsMiddleClick(Player player)
		{
			if (!player || player != Player.m_localPlayer) return false;
			if (IsActive) return OwnsPlayerInput;
			if (!InfinityHammerBridge.TryGetActiveToolSelectionName(out string selectedTool)
			    || !ToolInstaller.IsPieceRepeaterToolName(selectedTool))
			{
				return false;
			}

			CaptureAndActivate();
			return IsActive;
		}
		internal static bool ShouldSuppressPauseMenu => false;

		// Player.Update's prefix captures the settings key before vanilla build actions.
		// Tick also calls this for frames where the player update did not run first.
		internal static bool TryConsumeSettingsHotkey()
		{
			PieceRepeaterController controller = _instance;
			if (controller == null || !controller._active || !Player.m_localPlayer) return false;
			KeyCode key = PluginConfig.PieceRepeaterSettingsHotkey.Value;
			if (key == KeyCode.None || key == KeyCode.Escape || !Input.GetKeyDown(key)) return false;
			if (controller._settingsPanel.IsOpen)
			{
				controller._settingsPanel.Close(true);
				return true;
			}
			if (!OwnsPlayerInput) return false;
			if (PrecisionAreaPipetteInput.IsTextInputBlocking()) return false;
			if (!InfinityHammerBridge.TryGetActiveToolSelectionName(out string selectedTool)
			    || !ToolInstaller.IsPieceRepeaterToolName(selectedTool)) return false;
			controller._settingsPanel.Open();
			return controller._settingsPanel.IsOpen;
		}

		internal PieceRepeaterController()
		{
			_instance = this;
			_settingsPanel = new PieceRepeaterSettingsPanel(this);
		}

		internal static void CaptureAndActivate()
		{
			if (_instance == null || _instance._active)
			{
				return;
			}

			_instance._prefabId = InfinityHammerBridge.TryGetSelectedBuildPrefabId(out string prefabId)
				? prefabId
				: null;
			_instance.Activate();
		}

		internal void Activate()
		{
			if (InfinityHammerBridge.TrySuspendShapeShortcut(out IDisposable restoreShapeShortcut))
				_restoreIhShapeShortcut = restoreShapeShortcut;
			_active = true;
			_mode = PieceRepeaterMode.Path;
			_filled = false;
			_bothSides = false;
			_hasLockedStartPlacement = false;
			_snapPointIndex = -1;
			_gapMeters = Mathf.Clamp(PluginConfig.PieceRepeaterGap.Value, 0f, 5f);
			_rotationSteps = 0;
			_sideOffset = 2f;
			_points.Clear();
			_lastPathDiagnosticKey = null;
			_lastPathDiagnosticPointCount = -1;
			_awaitPrimaryRelease = true;
			EnsurePreview();
			UpdatePreview();
			Notify(_prefabId == null
				? "Piece Repeater: middle-click a placed build piece to choose it."
				: "Piece Repeater ready: " + _prefabId + ". Settings: " + PluginConfig.PieceRepeaterSettingsHotkey.Value + "; click path points; Ctrl+click places.");
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[PieceRepeater] Activated; prefab=" + (_prefabId ?? "<sample required>"));
		}

		internal void Deactivate(string reason = "cancel")
		{
			if (!_active)
			{
				return;
			}

			_active = false;
			_settingsPanel.Close(false);
			if (_keyHints != null) KeyHintManager.Instance.RemoveKeyHint(_keyHints);
			_keyHints = null;
			_restoreIhShapeShortcut?.Dispose();
			_restoreIhShapeShortcut = null;
			_points.Clear();
			_lastPathDiagnosticKey = null;
			_lastPathDiagnosticPointCount = -1;
			_hasLockedStartPlacement = false;
			_snapPointIndex = -1;
			_placements.Clear();
			_outline.Clear();
			_awaitPrimaryRelease = false;
			ClearPreview();
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[PieceRepeater] Deactivated: " + reason + ".");
		}

		internal void Shutdown()
		{
			Deactivate("plugin-destroy");
			_settingsPanel.Shutdown();
			ClearPreview();
			if (_instance == this) _instance = null;
			IsPlacingBatch = false;
		}

		internal static void OnOtherBuildPieceSelected(Piece piece)
		{
			if (IsActive && piece && !ToolIdentityTracker.IsOurPiece(piece))
			{
				_instance.Deactivate("other-build-piece");
			}
		}

		internal void Tick()
		{
			if (!_active && Player.m_localPlayer && Player.m_localPlayer.InPlaceMode()
			    && InfinityHammerBridge.TryGetActiveToolSelectionName(out string armingTool)
			    && ToolInstaller.IsPieceRepeaterToolName(armingTool))
			{
				CaptureAndActivate();
			}

			if (!_active)
			{
				return;
			}

			Player player = Player.m_localPlayer;
			if (!player)
			{
				Deactivate("no-player");
				return;
			}

			if (!player.InPlaceMode())
			{
				Deactivate("left-place-mode");
				return;
			}

			bool hasSelectedTool = InfinityHammerBridge.TryGetActiveToolSelectionName(out string selectedTool);
			bool shapeToolSelected = hasSelectedTool
				&& ToolInstaller.IsPieceRepeaterToolName(selectedTool);
			if (hasSelectedTool && !shapeToolSelected)
			{
				Deactivate("other-ih-tool");
				return;
			}

			if (Input.GetKeyDown(KeyCode.Escape))
			{
				bool panelWasOpen = _settingsPanel.IsOpen;
				Deactivate("escape");
				// A modal Jotunn panel blocks Valheim's normal Escape handler. Restore
				// the expected tool-exit-and-menu behavior on this same keypress.
				if (panelWasOpen && Menu.instance) Menu.instance.Show();
				return;
			}
			if (TryConsumeSettingsHotkey()) return;
			if (_settingsPanel.IsOpen)
			{
				return;
			}
			if (PrecisionAreaPipetteInput.IsTextInputBlocking()) return;
			HandleSnapPointInput(player);
			EnsureKeyHints(player);
			float wheel = Input.mouseScrollDelta.y;
			if (Mathf.Abs(wheel) > 0.01f)
			{
				if (Input.GetKey(PluginConfig.PieceRepeaterSpacingModifier.Value))
				{
					SetGap(_gapMeters + wheel * 0.1f);
					Notify("Piece gap: " + _gapMeters.ToString("0.##") + " m; repeat interval: " + GetSpacing().ToString("0.##") + " m.");
				}
				else
				{
					int rotationDelta = wheel > 0f ? 1 : -1;
					_rotationSteps = (_rotationSteps + rotationDelta + 16) % 16;
					Notify("Piece rotation: " + (_rotationSteps * RotationStepDegrees).ToString("0.#") + "°.");
				}
				UpdatePreview();
			}

			if (PrecisionAreaPipetteInput.WasMouseButtonDown(2))
			{
				SampleHoveredPiece(player);
				return;
			}

			_hasAim = PrecisionAreaPipetteInput.TryGetAimPoint(out _aim);
			UpdatePreview();
			if (_awaitPrimaryRelease)
			{
				if (!Input.GetMouseButton(0) && !Input.GetKey(KeyCode.Mouse0))
				{
					_awaitPrimaryRelease = false;
				}
				return;
			}

			if (!PrecisionAreaPipetteInput.WasMouseButtonDown(0) || !_hasAim)
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(_prefabId))
			{
				Notify("Piece Repeater: middle-click a placed building piece to sample it first.");
				return;
			}

			bool finish = Input.GetKey(PluginConfig.PieceRepeaterPlaceModifier.Value);
			if (_mode == PieceRepeaterMode.Path)
			{
				GameObject selectedPrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(_prefabId) : null;
				if (!selectedPrefab || PrefabPlacementMetadata.IsExcludedPiece(selectedPrefab)
				    || ResolvePlacementMetadata(selectedPrefab).Status != RepeatProfileStatus.Ready)
				{
					Notify("Piece Repeater: this piece needs a Ready profile in PieceProfiles.yml.");
					return;
				}
				if (_points.Count == 0 && ZNetScene.instance
				    && ZNetScene.instance.GetPrefab(_prefabId) is GameObject firstPrefab)
				{
					ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(firstPrefab);
					Placement startPlacement = GetInitialPlacement(firstPrefab, _aim, metadata);
					if (TryGetInitialSnapPlacement(player, _aim, out Placement snappedStart))
						startPlacement = snappedStart;
					_lockedStartPlacement = startPlacement;
					_rotationStepsAtStart = _rotationSteps;
					_hasLockedStartPlacement = true;
					_points.Add(startPlacement.Position + startPlacement.Rotation *
						(metadata.ProfileType == RepeatProfileType.EdgeChain ? metadata.JoinEnd : metadata.CenterOffset));
				}
				else if (_points.Count == 0)
				{
					_hasLockedStartPlacement = false;
					_points.Add(_aim);
				}
				else
				{
					// Later clicks define the route and must not release the first pose lock.
					if (_points.Count == 1 && _hasLockedStartPlacement)
					{
						ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(selectedPrefab);
						if (metadata.ProfileType == RepeatProfileType.EdgeChain)
							_points[0] = GetOutgoingJoin(metadata, _aim);
					}
					Vector3 waypoint = GetPathWaypoint(_aim);
					Vector3 fromLast = waypoint - _points[_points.Count - 1];
					fromLast.y = 0f;
					if (fromLast.sqrMagnitude < 0.0001f && !finish)
					{
						Notify("Waypoint remains at the previous joint; move farther or release the joint modifier.");
						return;
					}
					if (fromLast.sqrMagnitude >= 0.0001f) _points.Add(waypoint);
				}
				if (finish)
				{
					BuildAndPlace(player);
				}
				else
				{
					UpdatePreview();
					Notify("Path point " + _points.Count + "; Ctrl+click to finish and place.");
				}
				return;
			}

			if (_points.Count == 0)
			{
				_points.Add(_aim);
				UpdatePreview();
				Notify(_mode + ": set size with the cursor, then Ctrl+click to place.");
				return;
			}

			if (finish)
			{
				BuildAndPlace(player);
			}
			else
			{
				Notify("Ctrl+click to place the previewed " + _mode.ToString().ToLowerInvariant() + ".");
			}
		}

		internal float GapMeters => _gapMeters;

		internal void SetGap(float gap)
		{
			_gapMeters = Mathf.Round(Mathf.Clamp(gap, 0f, 5f) * 10f) / 10f;
			PluginConfig.PieceRepeaterGap.Value = _gapMeters;
			UpdatePreview();
		}

		internal void RefreshSettingsPreview() => UpdatePreview();

		internal void OnSettingsClosed()
		{
			_awaitPrimaryRelease = true;
			UpdatePreview();
		}

		private Vector3 GetOutgoingJoin(ResolvedPrefabPlacement metadata, Vector3 target)
		{
			Vector3 start = _lockedStartPlacement.Position + _lockedStartPlacement.Rotation * metadata.JoinStart;
			Vector3 end = _lockedStartPlacement.Position + _lockedStartPlacement.Rotation * metadata.JoinEnd;
			Vector2 startToTarget = new Vector2(start.x - target.x, start.z - target.z);
			Vector2 endToTarget = new Vector2(end.x - target.x, end.z - target.z);
			return startToTarget.sqrMagnitude < endToTarget.sqrMagnitude ? start : end;
		}

		private bool IsReverseEdgeChain(IList<Vector3> points, ResolvedPrefabPlacement metadata)
		{
			if (!_hasLockedStartPlacement || points.Count == 0 || metadata.ProfileType != RepeatProfileType.EdgeChain)
				return false;
			Vector3 start = _lockedStartPlacement.Position + _lockedStartPlacement.Rotation * metadata.JoinStart;
			Vector3 end = _lockedStartPlacement.Position + _lockedStartPlacement.Rotation * metadata.JoinEnd;
			Vector2 toStart = new Vector2(points[0].x - start.x, points[0].z - start.z);
			Vector2 toEnd = new Vector2(points[0].x - end.x, points[0].z - end.z);
			return toStart.sqrMagnitude < toEnd.sqrMagnitude;
		}

		private Vector3 GetPathWaypoint(Vector3 aim, IList<Vector3> pathPoints = null)
		{
			pathPoints = pathPoints ?? _points;
			if (pathPoints.Count == 0 || !Input.GetKey(PluginConfig.PieceRepeaterJointModifier.Value)) return aim;
			Vector3 last = pathPoints[pathPoints.Count - 1];
			Vector3 horizontal = aim - last;
			horizontal.y = 0f;
			float length = horizontal.magnitude;
			if (length < 0.001f) return last;
			float prior = 0f;
			for (int i = 1; i < pathPoints.Count; i++)
			{
				Vector3 delta = pathPoints[i] - pathPoints[i - 1];
				delta.y = 0f;
				prior += delta.magnitude;
			}
			float pitch = GetSpacing();
			float nearest = Mathf.Round((prior + length) / pitch) * pitch;
			float along = Mathf.Max(0f, nearest - prior);
			Vector3 point = last + horizontal / length * along;
			point.y = aim.y;
			return point;
		}

		internal static void InitializeKeyHints()
		{
			if (!KeyHints.instance) return;
			var manager = KeyHintManager.Instance;
			var type = manager.GetType();
			var current = HarmonyLib.AccessTools.Field(type, "KeyHintInstance")?.GetValue(manager) as KeyHints;
			if (current == KeyHints.instance) return;
			// The manager can first be requested by a ScriptEngine plugin after HUD.Start.
			// Run its existing initialization for that already-created HUD once.
			HarmonyLib.AccessTools.Method(type, "GetBaseGameObjects")?.Invoke(manager, new object[] { KeyHints.instance });
			HarmonyLib.AccessTools.Method(type, "KeyHints_Start")?.Invoke(manager, new object[] { KeyHints.instance });
		}

		private void EnsureKeyHints(Player player)
		{
			InitializeKeyHints();
			if (_keyHints != null) return;
			// ScriptEngine reloads replace this controller while Jotunn's static manager
			// survives. Remove this tool's prior registrations and objects before re-adding.
			var manager = KeyHintManager.Instance;
			var registered = HarmonyLib.AccessTools.Field(manager.GetType(), "KeyHints")?.GetValue(manager) as System.Collections.IDictionary;
			if (registered != null)
			{
				var stale = new List<KeyHintConfig>();
				foreach (System.Collections.DictionaryEntry entry in registered)
					if (entry.Value is KeyHintConfig old && old.Item == "Hammer" && old.Piece == "New Game Object") stale.Add(old);
				foreach (KeyHintConfig old in stale) manager.RemoveKeyHint(old);
			}
			// GetCurrentWeapon excludes tools and falls back to unarmed for a hammer.
			var item = HarmonyLib.AccessTools.Field(typeof(Humanoid), "m_rightItem")?.GetValue(player) as ItemDrop.ItemData;
			if (item == null)
				item = HarmonyLib.AccessTools.Field(typeof(Humanoid), "m_leftItem")?.GetValue(player) as ItemDrop.ItemData;
			// Jotunn matches the underlying table, whereas IH patches Player.GetSelectedPiece.
			var table = HarmonyLib.AccessTools.Field(typeof(Player), "m_buildPieces")?.GetValue(player) as PieceTable;
			Piece selected = table ? table.GetSelectedPiece() : null;
			if (item?.m_dropPrefab == null || !selected) return;
			var config = new KeyHintConfig
			{
				Item = item.m_dropPrefab.name,
				Piece = selected.name,
				ButtonConfigs = new[]
				{
					new ButtonConfig { Name = "Attack", Key = KeyCode.Mouse0, Hint = "Waypoint / " + PluginConfig.PieceRepeaterPlaceModifier.Value + ": Place" },
					new ButtonConfig { Name = "Remove", Key = KeyCode.Mouse2, Hint = "Sample" },
					new ButtonConfig { Name = "Mouse ScrollWheel", Axis = "Mouse ScrollWheel", Hint = "Rotate pieces / " + PluginConfig.PieceRepeaterSpacingModifier.Value + ": gap" },
					new ButtonConfig { Name = "TabRight", Key = KeyCode.E, Hint = "Cycle snap points (Q/E)" },
					new ButtonConfig { Name = "Run", Key = PluginConfig.PieceRepeaterLevelModifier.Value, Hint = "Level height; " + PluginConfig.PieceRepeaterJointModifier.Value + "+click: joint" }
				}
			};
			if (KeyHintManager.Instance.AddKeyHint(config))
			{
				_keyHints = config;
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[PieceRepeater] Registered key hints for " + config + ".");
			}
		}

		private void SampleHoveredPiece(Player player)
		{
			Piece hovering = player.GetHoveringPiece();
			if (!hovering)
			{
				Notify("Aim at a placed building piece and middle-click to sample it.");
				return;
			}

			ZNetView view = hovering.GetComponent<ZNetView>();
			if (!view)
			{
				Notify("That target is not a networked building piece.");
				return;
			}

			ZDO zdo = view.GetZDO();
			GameObject registered = zdo != null && ZNetScene.instance ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
			string prefab = registered ? Utils.GetPrefabName(registered) : Utils.GetPrefabName(view.gameObject);
			registered = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null;
			if (!registered || !registered.GetComponent<Piece>())
			{
				Notify("That prefab cannot be placed as a building piece.");
				return;
			}

			_prefabId = prefab;
			_points.Clear();
			_hasLockedStartPlacement = false;
			_snapPointIndex = -1;
			_sampledYaw = hovering.transform.eulerAngles.y;
			Notify("Piece Repeater selected " + prefab + ".");
		}

		private void BuildAndPlace(Player player)
		{
			if (!ZNetScene.instance)
			{
				Notify("Piece Repeater: prefab scene is unavailable.");
				_points.Clear();
				return;
			}

			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			if (!piece)
			{
				Notify("Piece Repeater: selected prefab is no longer available.");
				_prefabId = null;
				_points.Clear();
				return;
			}

			GenerateLayout(prefab);
			if (_placements.Count == 0)
			{
				Notify("Piece Repeater: add more points or increase the size.");
				_points.Clear();
				return;
			}
			if (_placements.Count > MaximumPlacements)
			{
				Notify("Piece Repeater: " + _placements.Count + " placements exceeds the safe limit of " + MaximumPlacements + ". Increase spacing or reduce the shape.");
				_points.Clear();
				return;
			}

			int placed = 0;
			if (!InfinityHammerBridge.TrySuspendPlacementInterception(out IDisposable restoreIhPlacement))
			{
				Notify("Piece Repeater: could not safely bypass IH's active tool placement hook; no pieces placed.");
				_points.Clear();
				UpdatePreview();
				return;
			}

			IsPlacingBatch = true;
			WorldEditUndoBridge.BeginAction();
			try
			{
				using (restoreIhPlacement)
				{
					foreach (Placement placement in _placements)
					{
						player.PlacePiece(piece, placement.Position, placement.Rotation, true);
						placed++;
					}
				}
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("[PieceRepeater] Placement stopped after " + placed + " pieces: " + ex.Message);
			}
			finally
			{
				WorldEditUndoBridge.EndAction();
				IsPlacingBatch = false;
			}

			Notify("Piece Repeater sent " + placed + " " + _prefabId + " placement request(s). Use IH undo to reverse the batch.");
			_points.Clear();
			_hasLockedStartPlacement = false;
			UpdatePreview();
		}

		private void GenerateLayout(GameObject prefab)
		{
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			if (_mode == PieceRepeaterMode.Path)
			{
				_placements.Clear();
				_outline.Clear();
				GeneratePathPreview(_points, GetSpacing(prefab), prefab);
				return;
			}
			GenerateLayoutCore(prefab);
			for (int i = 0; i < _placements.Count; i++)
			{
				float groundOffset = -metadata.BottomOffset;
				_placements[i] = AdjustPlacement(_placements[i], metadata, groundOffset,
					_points.Count > 0 ? _points[0].y : _placements[i].Position.y);
			}
			if (_mode == PieceRepeaterMode.Path && !_bothSides && _hasLockedStartPlacement && _placements.Count > 0)
				_placements[0] = _lockedStartPlacement;
		}

		private Placement AdjustPlacement(Placement placement, ResolvedPrefabPlacement metadata, float groundOffset, float fixedHeight)
		{
			Quaternion rotation = placement.Rotation;
			if (metadata.Axis == PrefabRepeatAxis.X)
				rotation *= Quaternion.Euler(0f, -90f, 0f);
			else if (metadata.Axis == PrefabRepeatAxis.Vertical)
				rotation = Quaternion.Euler(0f, _sampledYaw, 0f);
			rotation *= Quaternion.Euler(0f, metadata.YawOffset, 0f);
			// Path mode applies later wheel changes after solving all positions, so
			// rotation cannot drag the rest of the chain around the first joint.
			int pathSteps = _hasLockedStartPlacement ? _rotationSteps - _rotationStepsAtStart : _rotationSteps;
			if (_mode != PieceRepeaterMode.Path)
				rotation = Quaternion.Euler(0f, pathSteps * RotationStepDegrees, 0f) * rotation;
			Vector3 rootPosition = placement.Position - rotation * metadata.CenterOffset;
			rootPosition = ApplySurfaceHeightMode(rootPosition, groundOffset, fixedHeight);
			return new Placement(rootPosition, rotation);
		}

		private void GenerateLayoutCore(GameObject prefab)
		{
			_placements.Clear();
			_outline.Clear();
			float spacing = GetSpacing(prefab);
			if (_mode == PieceRepeaterMode.Path)
			{
				GeneratePath(spacing, _bothSides ? _sideOffset : 0f);
				return;
			}
			if (_points.Count == 0 || !_hasAim)
			{
				return;
			}

			Vector3 a = _points[0];
			if (_mode == PieceRepeaterMode.Circle)
			{
				float radius = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(_aim.x, _aim.z));
				if (radius < MinimumSpacing * 0.5f) return;
				if (_filled) GenerateCircleFill(a, radius, spacing);
				else GenerateCircleOutline(a, radius, spacing);
				return;
			}

			float dx = _aim.x - a.x;
			float dz = _aim.z - a.z;
			if (_mode == PieceRepeaterMode.Square)
			{
				float half = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) * 0.5f;
				if (half < MinimumSpacing * 0.5f) return;
				GenerateRectangle(a, a + new Vector3(half * 2f, 0f, half * 2f), spacing, square: true);
			}
			else
			{
				if (Mathf.Abs(dx) < MinimumSpacing * 0.5f || Mathf.Abs(dz) < MinimumSpacing * 0.5f) return;
				GenerateRectangle(a, _aim, spacing, square: false);
			}
		}

		private void GeneratePath(float spacing, float sideOffset)
		{
			if (_points.Count < 2) return;
			List<Placement> center = SamplePolyline(_points, spacing, _hasLockedStartPlacement && sideOffset <= 0f);
			if (center.Count == 0) return;
			if (sideOffset <= 0f)
			{
				_placements.AddRange(center);
				foreach (Vector3 p in _points) _outline.Add(p);
				return;
			}

			List<Vector3> leftLine = new List<Vector3>();
			List<Vector3> rightLine = new List<Vector3>();
			foreach (Placement p in center)
			{
				Vector3 forward = p.Rotation * Vector3.forward;
				Vector3 side = new Vector3(forward.z, 0f, -forward.x) * sideOffset;
				Vector3 left = p.Position + side;
				Vector3 right = p.Position - side;
				_placements.Add(new Placement(left, p.Rotation));
				_placements.Add(new Placement(right, p.Rotation));
				leftLine.Add(left);
				rightLine.Add(right);
			}
			_outline.AddRange(leftLine);
			EnsureSecondLine();
			SetLine(_secondLine, rightLine, false);
		}

		private void GenerateCircleOutline(Vector3 center, float radius, float spacing)
		{
			int count = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / spacing), 3, MaximumPlacements);
			for (int i = 0; i < count; i++)
			{
				float angle = 2f * Mathf.PI * i / count;
				Vector3 pos = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
				float tangent = angle * Mathf.Rad2Deg + 90f;
				_placements.Add(new Placement(pos, Quaternion.Euler(0f, tangent, 0f)));
				_outline.Add(pos);
			}
		}

		private void GenerateCircleFill(Vector3 center, float radius, float spacing)
		{
			float rowStep = spacing * 0.8660254f;
			int row = 0;
			for (float z = -radius; z <= radius; z += rowStep, row++)
			{
				float offset = (row & 1) == 0 ? 0f : spacing * 0.5f;
				for (float x = -radius + offset; x <= radius; x += spacing)
				{
					if (x * x + z * z > radius * radius) continue;
					_placements.Add(new Placement(center + new Vector3(x, 0f, z), Quaternion.identity));
					if (_placements.Count > MaximumPlacements) return;
				}
			}
			int segments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / spacing), 12, 96);
			for (int i = 0; i < segments; i++)
			{
				float angle = 2f * Mathf.PI * i / segments;
				_outline.Add(center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
			}
		}

		private void GenerateRectangle(Vector3 a, Vector3 b, float spacing, bool square)
		{
			float minX = Mathf.Min(a.x, b.x);
			float maxX = Mathf.Max(a.x, b.x);
			float minZ = Mathf.Min(a.z, b.z);
			float maxZ = Mathf.Max(a.z, b.z);
			if (square)
			{
				float side = Mathf.Max(maxX - minX, maxZ - minZ);
				Vector3 center = a;
				minX = center.x - side * 0.5f;
				maxX = center.x + side * 0.5f;
				minZ = center.z - side * 0.5f;
				maxZ = center.z + side * 0.5f;
			}

			Vector3[] corners =
			{
				new Vector3(minX, a.y, minZ), new Vector3(maxX, a.y, minZ),
				new Vector3(maxX, a.y, maxZ), new Vector3(minX, a.y, maxZ)
			};
			if (_filled)
			{
				int row = 0;
				for (float z = minZ + spacing * 0.5f; z <= maxZ; z += spacing, row++)
				{
					for (float x = minX + spacing * 0.5f; x <= maxX; x += spacing)
					{
						_placements.Add(new Placement(new Vector3(x, a.y, z), Quaternion.identity));
						if (_placements.Count > MaximumPlacements) break;
					}
					if (_placements.Count > MaximumPlacements) break;
				}
			}
			else
		{
				for (int side = 0; side < 4; side++)
				{
					Vector3 start = corners[side];
					Vector3 end = corners[(side + 1) % 4];
					float length = Vector3.Distance(start, end);
					int count = Mathf.Max(1, Mathf.CeilToInt(length / spacing));
					Vector3 dir = (end - start).normalized;
					Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
					for (int i = 0; i < count; i++)
					{
						_placements.Add(new Placement(Vector3.Lerp(start, end, (float)i / count), rot));
					}
				}
			}
			_outline.AddRange(corners);
		}

		private static List<Placement> SamplePolyline(IList<Vector3> points, float spacing, bool anchorFirst = false, IList<int> rawCounts = null, IList<int> duplicateCounts = null)
		{
			var result = new List<Placement>();
			if (points == null || points.Count < 2) return result;
			spacing = Mathf.Max(MinimumSpacing, spacing);
			var segmentLengths = new List<float>(points.Count - 1);
			var segmentDirections = new List<Vector3>(points.Count - 1);
			var segmentStartDistances = new List<float>(points.Count - 1);
			var segmentCandidateCounts = new List<int>(points.Count - 1);
			var segmentDuplicateCounts = new List<int>(points.Count - 1);
			float totalLength = 0f;
			for (int segment = 0; segment < points.Count - 1; segment++)
			{
				Vector3 start = points[segment];
				Vector3 end = points[segment + 1];
				Vector3 dir = end - start;
				dir.y = 0f;
				float length = dir.magnitude;
				segmentStartDistances.Add(totalLength);
				segmentLengths.Add(length);
				segmentDirections.Add(length >= MinimumSpacing * 0.5f ? dir / length : Vector3.zero);
				segmentCandidateCounts.Add(0);
				segmentDuplicateCounts.Add(0);
				totalLength += length;
			}

			if (totalLength < MinimumSpacing * 0.5f) return result;
		// Carry one fixed step interval across waypoint boundaries. This keeps
		// earlier centers in place as a new waypoint is added and avoids compressing
		// every leg independently to fit its endpoint.
		float firstDistance = anchorFirst ? 0f : spacing * 0.5f;
		int activeSegment = 0;
		for (float distance = firstDistance; distance <= totalLength + 0.001f; distance += spacing)
		{
			while (activeSegment < segmentLengths.Count - 1
			       && distance > segmentStartDistances[activeSegment] + segmentLengths[activeSegment] + 0.001f)
				activeSegment++;

			int segment = activeSegment;
			float length = segmentLengths[segment];
			if (length < MinimumSpacing * 0.5f)
			{
				int next = segment;
				while (next < segmentDirections.Count && segmentDirections[next] == Vector3.zero) next++;
				if (next >= segmentDirections.Count) continue;
				segment = next;
				length = segmentLengths[segment];
			}

			Vector3 direction = segmentDirections[segment];
			float localDistance = distance - segmentStartDistances[segment];
			float t = length > 0f ? Mathf.Clamp01(localDistance / length) : 0f;
			Vector3 position = Vector3.Lerp(points[segment], points[segment + 1], t);

			segmentCandidateCounts[segment]++;
			result.Add(new Placement(position, Quaternion.LookRotation(direction, Vector3.up)));
		}

		for (int i = 0; i < segmentCandidateCounts.Count; i++)
		{
			rawCounts?.Add(segmentCandidateCounts[i]);
			duplicateCounts?.Add(segmentDuplicateCounts[i]);
		}
			return result;
		}

		private ResolvedPrefabPlacement ResolvePlacementMetadata(GameObject prefab)
		{
			return PrefabPlacementMetadata.Resolve(_prefabId, prefab);
		}

		private void HandleSnapPointInput(Player player)
		{
			bool previous = ZInput.GetButtonDown("TabLeft");
			bool next = ZInput.GetButtonDown("TabRight");
			if (previous == next) return;
			if (!ZNetScene.instance || string.IsNullOrEmpty(_prefabId))
			{
				Notify("Select or sample a piece before cycling its snap points.");
				return;
			}

			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			if (!piece)
			{
				Notify("The selected prefab has no building piece snap points.");
				return;
			}

			var snapPoints = new List<Transform>();
			piece.GetSnapPoints(snapPoints);
			if (snapPoints.Count == 0)
			{
				Notify("This piece has no snap points to cycle.");
				return;
			}

			int stateCount = snapPoints.Count + 1; // Auto plus each source snap point.
			int state = _snapPointIndex + 1;
			state = (state + (next ? 1 : -1) + stateCount) % stateCount;
			_snapPointIndex = state - 1;
			string label = _snapPointIndex < 0 ? Localization.instance.Localize("$msg_snapping_auto") : snapPoints[_snapPointIndex].name;
			Notify(Localization.instance.Localize("$msg_snapping " + label));
			UpdatePreview();
		}

		private bool TryGetInitialSnapPlacement(Player player, Vector3 aim, out Placement placement)
		{
			placement = default(Placement);
			if (!player || !ZNetScene.instance || string.IsNullOrEmpty(_prefabId)) return false;
			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			if (!prefab || !EnsureSnapProbe(prefab)) return false;
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			Placement initial = GetInitialPlacement(prefab, aim, metadata);
			_snapProbe.transform.SetPositionAndRotation(initial.Position, initial.Rotation);
			if (!InfinityHammerBridge.TryFindSnapPair(player, _snapProbe.transform, _snapPointIndex,
				out Transform sourceSnapPoint, out Transform targetSnapPoint))
			{ placement = initial; return true; }

			Vector3 snappedRoot = initial.Position + targetSnapPoint.position - sourceSnapPoint.position;
			if (metadata.ProfileType == RepeatProfileType.GroundProp)
				snappedRoot = ApplySurfaceHeightMode(snappedRoot, -metadata.BottomOffset, snappedRoot.y);
			placement = new Placement(snappedRoot, initial.Rotation);
			return true;
		}

		private Vector3 GetWaypointPosition(Player player, Vector3 aim)
		{
			if (!ZNetScene.instance || string.IsNullOrEmpty(_prefabId)) return aim;
			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			if (!prefab) return aim;
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			if (!TryGetInitialSnapPlacement(player, aim, out Placement placement)) return aim;
			return placement.Position + placement.Rotation * metadata.CenterOffset;
		}

		private Placement GetInitialPlacement(GameObject prefab, Vector3 aim, ResolvedPrefabPlacement metadata)
		{
			Quaternion rotation = GetInitialPlacementRotation(metadata).normalized;
			float groundOffset = -metadata.BottomOffset;
			Vector3 rootPosition = aim - rotation * metadata.CenterOffset;
			rootPosition.y = aim.y + groundOffset;
			if (EnsureSnapProbe(prefab) && InfinityHammerBridge.TryGetBuildSurface(Player.m_localPlayer,
				out Vector3 point, out Vector3 normal, out bool terrain))
			{
				Piece piece = _snapProbe.GetComponent<Piece>();
				var snaps = new List<Transform>();
				piece.GetSnapPoints(snaps);
				_snapProbe.transform.SetPositionAndRotation(point, rotation);
				if (_snapPointIndex >= 0 && _snapPointIndex < snaps.Count)
					rootPosition = point - (snaps[_snapPointIndex].position - point);
				else if (terrain)
				{
					rootPosition = point - rotation * metadata.CenterOffset;
					rootPosition.y = point.y + groundOffset;
				}
				else if (piece.m_clipEverything)
					rootPosition = point;
				else
				{
					// Vanilla offsets the collider surface onto the hit before searching snap pairs.
					_snapProbe.transform.position = point + normal * 50f;
					float nearest = float.PositiveInfinity;
					Vector3 closest = point;
					foreach (Collider collider in _snapProbe.GetComponentsInChildren<Collider>(true))
					{
						if (!collider.enabled || collider.isTrigger) continue;
						if (!(collider is BoxCollider) && !(collider is SphereCollider) && !(collider is CapsuleCollider)
							&& !(collider is MeshCollider mesh && mesh.convex)) continue;
						Vector3 candidate = Physics.ClosestPoint(point, collider, collider.transform.position, collider.transform.rotation.normalized);
						float distance = Vector3.Distance(candidate, point);
						// A failed query can return the input point. It cannot be a valid
						// contact for a collider deliberately moved 50 metres away.
						if (distance > 0.001f && !float.IsNaN(distance) && distance < nearest)
						{ nearest = distance; closest = candidate; }
					}
					rootPosition = float.IsInfinity(nearest) ? point : point + _snapProbe.transform.position - closest;
				}
			}
			if (metadata.ProfileType == RepeatProfileType.GroundProp)
				rootPosition = ApplySurfaceHeightMode(rootPosition, groundOffset, rootPosition.y);
			return new Placement(rootPosition, rotation);
		}

		private bool EnsureSnapProbe(GameObject prefab)
		{
			if (_snapProbe && string.Equals(_snapProbePrefabId, _prefabId, StringComparison.OrdinalIgnoreCase))
				return true;
			ClearSnapProbe();
			_snapProbeContainer = new GameObject("SonicWorldTools_PieceRepeaterSnapProbe") { hideFlags = HideFlags.HideAndDontSave };
			_snapProbeContainer.SetActive(false);
			UnityEngine.Object.DontDestroyOnLoad(_snapProbeContainer);
			_snapProbe = UnityEngine.Object.Instantiate(prefab, _snapProbeContainer.transform, false);
			_snapProbe.name = "PieceRepeaterSnapProbe";
			foreach (Component component in _snapProbe.GetComponentsInChildren<Component>(true))
				if (!(component is Piece) && !(component is Transform) && !(component is Collider))
					UnityEngine.Object.Destroy(component);
			_snapProbe.SetActive(true);
			_snapProbePrefabId = _prefabId;
			return true;
		}

		private void ClearSnapProbe()
		{
			if (_snapProbeContainer) UnityEngine.Object.Destroy(_snapProbeContainer);
			_snapProbeContainer = null;
			_snapProbe = null;
			_snapProbePrefabId = null;
		}

		private Quaternion GetInitialPlacementRotation(ResolvedPrefabPlacement metadata)
		{
			Quaternion rotation = Quaternion.Euler(0f, _sampledYaw, 0f);
			if (metadata.ProfileType == RepeatProfileType.EdgeChain && metadata.Axis == PrefabRepeatAxis.X)
				rotation *= Quaternion.Euler(0f, -90f, 0f);
			rotation *= Quaternion.Euler(0f, metadata.YawOffset, 0f);
			return Quaternion.Euler(0f, _rotationSteps * RotationStepDegrees, 0f) * rotation;
		}

		private float GetSpacing(GameObject prefab = null)
		{
			if (!prefab && ZNetScene.instance && !string.IsNullOrEmpty(_prefabId))
				prefab = ZNetScene.instance.GetPrefab(_prefabId);
			return Mathf.Max(MinimumSpacing, ResolvePlacementMetadata(prefab).Spacing + _gapMeters);
		}

		private void EnsurePreview()
		{
			if (_previewRoot) return;
			_previewRoot = new GameObject("SonicWorldTools_PieceRepeaterPreview") { hideFlags = HideFlags.HideAndDontSave };
			UnityEngine.Object.DontDestroyOnLoad(_previewRoot);
			Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
			_previewMaterial = new Material(shader) { color = new Color(0.25f, 0.9f, 1f, 0.85f), hideFlags = HideFlags.HideAndDontSave };
			_pieceGhostMaterial = new Material(shader) { color = new Color(0.35f, 0.9f, 1f, 0.42f), hideFlags = HideFlags.HideAndDontSave };
			_outlineLine = CreateLine("Outline", 0.035f);
			_secondLine = CreateLine("SecondLine", 0.035f);
		}

		private LineRenderer CreateLine(string name, float width)
		{
			var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
			go.transform.SetParent(_previewRoot.transform, false);
			var line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.material = _previewMaterial;
			line.startColor = line.endColor = new Color(0.25f, 0.9f, 1f, 0.85f);
			line.startWidth = line.endWidth = width;
			line.enabled = false;
			return line;
		}

		private void EnsureSecondLine()
		{
			if (!_secondLine) _secondLine = CreateLine("SecondLine", 0.035f);
		}

		private void UpdatePreview()
		{
			if (!_active) return;
			EnsurePreview();
			_placements.Clear();
			_outline.Clear();
			if (_points.Count > 0 && _hasAim && ZNetScene.instance && !string.IsNullOrEmpty(_prefabId))
			{
				GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
				if (prefab)
				{
					if (_mode == PieceRepeaterMode.Path)
					{
						var pending = new List<Vector3>(_points);
						if (pending.Count == 1 && _hasLockedStartPlacement)
						{
							ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
							if (metadata.ProfileType == RepeatProfileType.EdgeChain)
								pending[0] = GetOutgoingJoin(metadata, _aim);
						}
						pending.Add(GetPathWaypoint(_aim, pending));
						if (pending.Count >= 2) GeneratePathPreview(pending, GetSpacing(prefab), prefab);
					}
					else if (_mode == PieceRepeaterMode.Circle || _points.Count >= 1)
					{
						var original = _points;
						GenerateLayoutWithAim(prefab, original[0], _aim);
					}
				}
			}
			if (_points.Count == 0 && _hasAim)
			{
				float radius = Mathf.Max(0.25f, GetSpacing() * 0.25f);
				for (int i = 0; i < 20; i++)
				{
					float angle = 2f * Mathf.PI * i / 20f;
					_outline.Add(_aim + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
				}
			}
			SetLine(_outlineLine, _outline, _mode != PieceRepeaterMode.Path || _points.Count == 0);
			if (!_bothSides || _mode != PieceRepeaterMode.Path || _points.Count == 0) _secondLine.enabled = false;
			UpdatePieceGhosts();
		}

		private void GenerateLayoutWithAim(GameObject prefab, Vector3 start, Vector3 aim)
		{
			Vector3 savedAim = _aim;
			_aim = aim;
			GenerateLayout(prefab);
			_aim = savedAim;
		}

		private void GeneratePathPreview(IList<Vector3> points, float spacing, GameObject prefab)
		{
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			IList<Vector3> layoutPoints = PluginConfig.PieceRepeaterRoundedCorners.Value
				? RoundOutgoingCorners(points, Mathf.Clamp(PluginConfig.PieceRepeaterCornerRadius.Value, 0.25f, 6f))
				: points;
			float groundOffset = -metadata.BottomOffset;
			float fixedHeight = _points.Count > 0 ? _points[0].y : 0f;
			var rawCounts = new List<int>();
			var duplicateCounts = new List<int>();
			bool reverseEdgeChain = IsReverseEdgeChain(points, metadata);
			Vector3 chainStart = reverseEdgeChain ? metadata.JoinEnd : metadata.JoinStart;
			Vector3 chainEnd = reverseEdgeChain ? metadata.JoinStart : metadata.JoinEnd;
			_lastPathCenterDeviation = 0f;
			if (_bothSides)
			{
				List<Placement> center = SamplePolyline(layoutPoints, spacing, _hasLockedStartPlacement && !_bothSides, rawCounts, duplicateCounts);
				var left = new List<Vector3>();
				var right = new List<Vector3>();
				foreach (Placement p in center)
				{
					Vector3 f = p.Rotation * Vector3.forward;
					Vector3 side = new Vector3(f.z, 0f, -f.x) * _sideOffset;
					Vector3 leftPos = ApplySurfaceHeightMode(p.Position + side, groundOffset, fixedHeight);
					Vector3 rightPos = ApplySurfaceHeightMode(p.Position - side, groundOffset, fixedHeight);
					left.Add(leftPos);
					right.Add(rightPos);
					_placements.Add(AdjustPlacement(new Placement(p.Position + side, p.Rotation), metadata, groundOffset, fixedHeight));
					_placements.Add(AdjustPlacement(new Placement(p.Position - side, p.Rotation), metadata, groundOffset, fixedHeight));
				}
				_outline.AddRange(left);
				SetLine(_secondLine, right, false);
			}
			else
			{
				foreach (Vector3 point in layoutPoints)
					_outline.Add(ApplySurfaceHeightMode(point, groundOffset, fixedHeight));
				List<Placement> center = SamplePolyline(layoutPoints, spacing, metadata.ProfileType == RepeatProfileType.GroundProp, rawCounts, duplicateCounts);
				if (_hasLockedStartPlacement) _placements.Add(_lockedStartPlacement);
				for (int i = 0; i < center.Count; i++)
				{
					if (metadata.ProfileType == RepeatProfileType.GroundProp && i == 0 && _hasLockedStartPlacement)
						continue;
					Placement proposed = center[i];
					if (metadata.ProfileType == RepeatProfileType.GroundProp)
					{
						Quaternion rotation = PluginConfig.PieceRepeaterPropFollowCurve.Value
							? proposed.Rotation * Quaternion.Euler(0f, metadata.TangentYawOffset, 0f)
							: _lockedStartPlacement.Rotation;
						Vector3 position = proposed.Position - rotation * metadata.CenterOffset;
						position = ApplySurfaceHeightMode(position, groundOffset, fixedHeight);
						_placements.Add(new Placement(position, rotation));
					}
					else if (metadata.ProfileType == RepeatProfileType.EdgeChain && _placements.Count > 0)
					{
						Placement previous = _placements[_placements.Count - 1];
						// Keep the sampled path tangent. Steering toward a center behind or beside
						// this joint makes successive pieces alternate direction on straight runs.
						if (reverseEdgeChain)
							proposed.Rotation = Quaternion.Euler(0f, 180f, 0f) * proposed.Rotation;
						Placement connected = AdjustPlacement(proposed, metadata, groundOffset, fixedHeight);
						Vector3 previousDirection = previous.Rotation * (chainEnd - chainStart);
						Vector3 nextDirection = connected.Rotation * (chainEnd - chainStart);
						previousDirection.y = nextDirection.y = 0f;
						previousDirection.Normalize();
						nextDirection.Normalize();
						float turn = Vector3.SignedAngle(previousDirection, nextDirection, Vector3.up);
						Vector3 previousSide = new Vector3(-previousDirection.z, 0f, previousDirection.x);
						// Pivot on the inside of the bend. The previous outside-side rule
						// pushed square floor modules across their intended corner and caused
						// the visible clipping reported for black-marble and stone floors.
						float turnSide = turn < 0f ? 1f : -1f;
						Vector3 previousCornerSide = Mathf.Abs(turn) < 1f
							? Vector3.zero
							: previousSide * turnSide;
						Vector3 nextSide = new Vector3(-nextDirection.z, 0f, nextDirection.x);
						Vector3 nextCornerSide = Mathf.Abs(turn) < 1f ? Vector3.zero : nextSide * turnSide;
						Vector3 previousJoin = GetEdgeJoinPoint(previous, prefab, metadata, chainEnd, previousCornerSide);
						Vector3 nextJoinOffset = GetEdgeJoinPoint(new Placement(Vector3.zero, connected.Rotation), prefab, metadata, chainStart, nextCornerSide);
						Vector3 previousCenter = previous.Position + previous.Rotation * chainEnd;
						Vector3 nextCenterOffset = connected.Rotation * chainStart;
						Vector3 centeredRoot = previousCenter + nextDirection * _gapMeters - nextCenterOffset;
						Vector3 cornerRoot = previousJoin + nextDirection * _gapMeters - nextJoinOffset;
						// Square modules have real snap points at all four footprint corners,
						// so use their matching inner corners directly. Long narrow pieces keep
						// a softer center-to-corner blend to avoid swinging the far end wide.
						Bounds profileBounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
						float footprintLong = Mathf.Max(profileBounds.size.x, profileBounds.size.z);
						float footprintShort = Mathf.Min(profileBounds.size.x, profileBounds.size.z);
						bool hasSquareFootprint = footprintLong > 0.01f && footprintShort / footprintLong >= 0.8f;
						float cornerBlend = hasSquareFootprint
							? Mathf.Clamp01(Mathf.Abs(turn) / 45f)
							: Mathf.Min(0.5f, Mathf.Abs(turn) / 90f * 0.5f);
						connected.Position.x = Mathf.Lerp(centeredRoot.x, cornerRoot.x, cornerBlend);
						connected.Position.z = Mathf.Lerp(centeredRoot.z, cornerRoot.z, cornerBlend);
						connected.Position = ApplySurfaceHeightMode(connected.Position, groundOffset, fixedHeight);
						Vector3 actualCenter = connected.Position + connected.Rotation * metadata.CenterOffset;
						Vector3 desiredCenter = proposed.Position;
						actualCenter.y = desiredCenter.y = 0f;
						_lastPathCenterDeviation = Mathf.Max(_lastPathCenterDeviation, Vector3.Distance(actualCenter, desiredCenter));
						_placements.Add(connected);
					}
					else _placements.Add(AdjustPlacement(proposed, metadata, groundOffset, fixedHeight));
				}
				if (_hasLockedStartPlacement)
				{
				int wheelDelta = _rotationSteps - _rotationStepsAtStart;
				if (wheelDelta != 0)
				{
					Quaternion wheelRotation = Quaternion.Euler(0f, wheelDelta * RotationStepDegrees, 0f);
					for (int i = 1; i < _placements.Count; i++)
					{
						Placement placement = _placements[i];
						placement.Rotation = wheelRotation * placement.Rotation;
						_placements[i] = placement;
					}
				}
				}
			}
			LogPathDiagnostics(points, spacing, metadata, rawCounts, duplicateCounts, prefab);
		}

		private static Vector3 GetEdgeJoinPoint(Placement placement, GameObject prefab,
			ResolvedPrefabPlacement metadata, Vector3 centerJoin, Vector3 preferredWorldSide)
		{
			if (preferredWorldSide.sqrMagnitude < 0.0001f)
				return placement.Position + placement.Rotation * centerJoin;

			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			Vector3 transverse = metadata.Axis == PrefabRepeatAxis.X ? Vector3.forward : Vector3.right;
			float halfWidth = metadata.Axis == PrefabRepeatAxis.X ? bounds.extents.z : bounds.extents.x;
			if (halfWidth <= 0.001f) return placement.Position + placement.Rotation * centerJoin;

			// Use the prefab's real snap transforms at a bend. The low snap pair is
			// what the Hammer uses for ground-level wall/floor joins; choose its inner
			// or outer member by projecting the turn side into the piece's local frame.
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			if (piece)
			{
				var snapPoints = new List<Transform>();
				piece.GetSnapPoints(snapPoints);
				Vector3 desiredLocalSide = Quaternion.Inverse(placement.Rotation) * preferredWorldSide;
				float desiredTransverse = (metadata.Axis == PrefabRepeatAxis.X ? bounds.center.z : bounds.center.x)
					+ (Vector3.Dot(desiredLocalSide, transverse) >= 0f ? halfWidth : -halfWidth);
				float targetAxis = metadata.Axis == PrefabRepeatAxis.X ? centerJoin.x : centerJoin.z;
				float targetBottom = bounds.min.y;
				Transform best = null;
				float bestScore = float.PositiveInfinity;
				foreach (Transform snap in snapPoints)
				{
					if (!snap) continue;
					Vector3 local = prefab.transform.InverseTransformPoint(snap.position);
					float axis = metadata.Axis == PrefabRepeatAxis.X ? local.x : local.z;
					float across = metadata.Axis == PrefabRepeatAxis.X ? local.z : local.x;
					float score = Mathf.Abs(axis - targetAxis) * 8f
						+ Mathf.Abs(across - desiredTransverse) * 4f
						+ Mathf.Abs(local.y - targetBottom) * 4f;
					if (score < bestScore) { bestScore = score; best = snap; }
				}
				if (best)
				{
					Vector3 local = prefab.transform.InverseTransformPoint(best.position);
					return placement.Position + placement.Rotation * local;
				}
			}

			Vector3 worldPositive = placement.Rotation * transverse;
			float side = Vector3.Dot(worldPositive, preferredWorldSide) >= 0f ? 1f : -1f;
			return placement.Position + placement.Rotation * (centerJoin + transverse * (side * halfWidth));
		}

		// An outgoing-only Bezier keeps every completed leg fixed when a new
		// waypoint is added. The new leg leaves its corner along the incoming
		// tangent, then eases into the new direction.
		private static List<Vector3> RoundOutgoingCorners(IList<Vector3> points, float radius)
		{
			var result = new List<Vector3>();
			if (points == null || points.Count == 0) return result;
			result.Add(points[0]);
			for (int leg = 0; leg + 1 < points.Count; leg++)
			{
				Vector3 start = points[leg];
				Vector3 end = points[leg + 1];
				Vector3 outgoing = end - start;
				outgoing.y = 0f;
				float length = outgoing.magnitude;
				if (leg > 0 && length >= 0.75f)
				{
					Vector3 incoming = start - points[leg - 1];
					incoming.y = 0f;
					if (incoming.sqrMagnitude > 0.01f)
					{
						incoming.Normalize();
						outgoing /= length;
						float turn = Vector3.Angle(incoming, outgoing);
						if (turn >= 8f && turn <= 145f)
						{
							float transition = Mathf.Min(radius, length * 0.45f);
							Vector3 p0 = start;
							Vector3 p3 = start + outgoing * transition;
							p3.y = Mathf.Lerp(start.y, end.y, transition / length);
							Vector3 p1 = p0 + incoming * (transition * 0.35f);
							p1.y = Mathf.Lerp(p0.y, p3.y, 0.33f);
							Vector3 p2 = p3 - outgoing * (transition * 0.35f);
							p2.y = Mathf.Lerp(p0.y, p3.y, 0.67f);
							int samples = Mathf.Clamp(Mathf.CeilToInt(transition / 0.25f), 3, 32);
							for (int i = 1; i <= samples; i++)
							{
								float t = (float)i / samples;
								float u = 1f - t;
								result.Add(u * u * u * p0 + 3f * u * u * t * p1
									+ 3f * u * t * t * p2 + t * t * t * p3);
							}
						}
					}
				}
				result.Add(end);
			}
			return result;
		}

		private void LogPathDiagnostics(IList<Vector3> points, float spacing, ResolvedPrefabPlacement metadata, IList<int> rawCounts, IList<int> duplicateCounts, GameObject prefab)
		{
			if (!PluginConfig.IsVerboseLogging() || points == null) return;
			// The preview rebuilds every frame. Emit only when a point/cursor moves by 0.5m.
			var key = new System.Text.StringBuilder();
			key.Append(_prefabId).Append('|').Append(points.Count).Append('|').Append(Mathf.RoundToInt(_aim.x * 2f)).Append(',').Append(Mathf.RoundToInt(_aim.z * 2f));
			for (int i = 0; i < points.Count; i++)
				key.Append('|').Append(Mathf.RoundToInt(points[i].x * 2f)).Append(',').Append(Mathf.RoundToInt(points[i].z * 2f));
			string signature = key.ToString();
			bool pointAdded = points.Count != _lastPathDiagnosticPointCount;
			bool movedFarEnough = Vector2.Distance(new Vector2(_aim.x, _aim.z), new Vector2(_lastPathDiagnosticAim.x, _lastPathDiagnosticAim.z)) >= 4f;
			if (!pointAdded && !movedFarEnough) return;
			if (string.Equals(signature, _lastPathDiagnosticKey, StringComparison.Ordinal)) return;
			_lastPathDiagnosticKey = signature;
			_lastPathDiagnosticPointCount = points.Count;
			_lastPathDiagnosticAim = _aim;

			var details = new System.Text.StringBuilder();
			Bounds renderedBounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float renderedAxisMin = metadata.Axis == PrefabRepeatAxis.X
				? renderedBounds.min.x
				: renderedBounds.min.z;
			float renderedAxisMax = metadata.Axis == PrefabRepeatAxis.X
				? renderedBounds.max.x
				: renderedBounds.max.z;
			details.Append("[PieceRepeater] PathTrace prefab=").Append(_prefabId)
				.Append(" profileType=").Append(metadata.ProfileType)
				.Append(" axis=").Append(metadata.Axis)
				.Append(" metadataSpacing=").Append(metadata.Spacing.ToString("0.###"))
				.Append(" gapMeters=").Append(_gapMeters.ToString("0.###"))
				.Append(" effectiveSpacing=").Append(spacing.ToString("0.###"))
				.Append(" centerOffset=").Append(metadata.CenterOffset.ToString("F2"))
				.Append(" yawOffset=").Append(metadata.YawOffset.ToString("0.###"))
				.Append(" tangentYawOffset=").Append(metadata.TangentYawOffset.ToString("0.###"))
				.Append(" renderLocal=").Append(renderedBounds.center.ToString("F2"))
				.Append(" renderSize=").Append(renderedBounds.size.ToString("F2"))
				.Append(" renderAxisEdges=").Append(renderedAxisMin.ToString("0.###")).Append("..").Append(renderedAxisMax.ToString("0.###"))
				.Append(" rotationSteps=").Append(_rotationSteps)
					.Append(" rotationStepsAtStart=").Append(_rotationStepsAtStart)
					.Append(" pathRotationDelta=").Append(_hasLockedStartPlacement ? _rotationSteps - _rotationStepsAtStart : _rotationSteps)
				.Append(" outgoingJoin=").Append(IsReverseEdgeChain(points, metadata) ? "start" : "end")
				.Append(" snapLocal=");
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			if (piece)
			{
				var snapPoints = new List<Transform>();
				piece.GetSnapPoints(snapPoints);
				for (int i = 0; i < snapPoints.Count; i++)
				{
					if (!snapPoints[i]) continue;
					if (i > 0) details.Append(',');
					Vector3 local = prefab.transform.InverseTransformPoint(snapPoints[i].position);
					details.Append(snapPoints[i].name).Append('@').Append(local.ToString("F2"));
				}
			}
			details.Append(" legs=");
			for (int i = 0; i + 1 < points.Count; i++)
			{
				if (i > 0) details.Append(';');
				float length = Vector2.Distance(new Vector2(points[i].x, points[i].z), new Vector2(points[i + 1].x, points[i + 1].z));
				int candidates = i < rawCounts.Count ? rawCounts[i] : 0;
				int skipped = i < duplicateCounts.Count ? duplicateCounts[i] : 0;
				details.Append(i).Append(':').Append(points[i].ToString("F1")).Append("->").Append(points[i + 1].ToString("F1"))
					.Append("/").Append(length.ToString("0.##")).Append("m/").Append(candidates)
					.Append(" candidates/").Append(candidates - skipped).Append(" kept/").Append(skipped).Append(" overlap-skipped");
			}
			details.Append(" final=").Append(_placements.Count)
				.Append(" centerDeviationMaxXZ=").Append(_lastPathCenterDeviation.ToString("0.###"))
				.Append(" rootSteps=");
			if (metadata.ProfileType == RepeatProfileType.EdgeChain && _placements.Count > 1)
			{
				bool reverseEdgeChain = IsReverseEdgeChain(points, metadata);
				Vector3 chainStart = reverseEdgeChain ? metadata.JoinEnd : metadata.JoinStart;
				Vector3 chainEnd = reverseEdgeChain ? metadata.JoinStart : metadata.JoinEnd;
				float maxJointGapError = 0f;
				int gapErrors = 0;
				for (int i = 1; i < _placements.Count; i++)
				{
					Vector3 previousEnd = _placements[i - 1].Position + _placements[i - 1].Rotation * chainEnd;
					Vector3 nextStart = _placements[i].Position + _placements[i].Rotation * chainStart;
					previousEnd.y = nextStart.y = 0f;
					float actualGap = Vector3.Distance(previousEnd, nextStart);
					float error = Mathf.Abs(actualGap - _gapMeters);
					maxJointGapError = Mathf.Max(maxJointGapError, error);
					if (error > 0.02f) gapErrors++;
				}
				details.Append(" jointGapTargetXZ=").Append(_gapMeters.ToString("0.###"))
					.Append(" jointGapErrorMaxXZ=").Append(maxJointGapError.ToString("0.###"))
					.Append(" jointsOver2cm=").Append(gapErrors);
			}
			int logged = Mathf.Min(_placements.Count, 24);
			for (int i = 0; i < logged; i++)
			{
				if (i > 0) details.Append(',');
				Placement placement = _placements[i];
				details.Append(placement.Position.ToString("F1"));
				if (i > 0)
				{
					float interval = Vector3.Distance(_placements[i - 1].Position, placement.Position);
					if (interval > spacing * 1.2f || interval < spacing * 0.7f)
						details.Append('(').Append(interval.ToString("0.00")).Append("m)");
				}
			}
			if (_placements.Count > logged) details.Append(",...");
			SonicWorldToolsPlugin.ModLogger?.LogInfo(details.ToString());
		}

		private Vector3 ApplyHeightMode(Vector3 position, float groundOffset, float fixedHeight)
		{
			if (IsSameHeightHeld() && _points.Count > 0)
			{
				if (TryGetGroundHeight(_points[0], out float firstGroundHeight))
				{
					position.y = firstGroundHeight + groundOffset;
				}
				else position.y = fixedHeight + groundOffset;
			}
			else if (TryGetGroundHeight(position, out float groundHeight))
			{
				position.y = groundHeight + groundOffset;
			}
			else position.y += groundOffset;
			return position;
		}

		private Vector3 ApplySurfaceHeightMode(Vector3 position, float groundOffset, float fixedHeight)
		{
			// Level mode preserves the established route height. Otherwise, use the
			// highest build-piece surface under the profile's footprint, then terrain.
			if (IsSameHeightHeld() && _points.Count > 0)
			{
				position.y = fixedHeight;
				return position;
			}

			if (TryGetBuildPieceSurfaceHeight(position, groundOffset, out float pieceHeight))
				position.y = pieceHeight + groundOffset;
			else if (TryGetGroundHeight(position, out float groundHeight))
				position.y = groundHeight + groundOffset;
			else
				position.y += groundOffset;
			return position;
		}

		private bool TryGetBuildPieceSurfaceHeight(Vector3 position, float groundOffset, out float surfaceHeight)
		{
			surfaceHeight = 0f;
			float rayStartY = position.y + Mathf.Max(2f, groundOffset + 1f);
			float rayDistance = Mathf.Max(12f, groundOffset + 10f);
			Vector3 origin = new Vector3(position.x, rayStartY, position.z);
			int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _surfaceRayHits,
				rayDistance, ~0, QueryTriggerInteraction.Ignore);
			float highestSupport = float.NegativeInfinity;
			Player localPlayer = Player.m_localPlayer;
			for (int i = 0; i < hitCount; i++)
			{
				Collider collider = _surfaceRayHits[i].collider;
				if (!collider || collider.isTrigger) continue;
				Transform hitTransform = collider.transform;
				if ((_previewRoot && hitTransform.IsChildOf(_previewRoot.transform))
				    || (_snapProbeContainer && hitTransform.IsChildOf(_snapProbeContainer.transform))
				    || (localPlayer && hitTransform.IsChildOf(localPlayer.transform)))
					continue;

				Piece piece = collider.GetComponentInParent<Piece>();
				if (!piece) continue;
				float hitY = _surfaceRayHits[i].point.y;
				// The ray begins above the intended contact height. Ignore overhead
				// pieces, and do not snap down to a distant lower floor by accident.
				if (hitY > position.y + 0.1f || hitY < position.y - rayDistance + 1f) continue;
				if (hitY > highestSupport) highestSupport = hitY;
			}

			if (float.IsNegativeInfinity(highestSupport)) return false;
			surfaceHeight = highestSupport;
			return true;
		}

		private static bool TryGetGroundHeight(Vector3 position, out float groundHeight)
		{
			groundHeight = 0f;
			if (!ZoneSystem.instance) return false;
			ZoneSystem.instance.GetSolidHeight(position, out groundHeight, 0);
			// GetSolidHeight can return its zero default when the requested column has no
			// terrain sample. Never send a piece at y=offset when the aimed terrain is far away.
			return !float.IsNaN(groundHeight)
			       && !float.IsInfinity(groundHeight)
			       && Mathf.Abs(groundHeight - position.y) <= 40f;
		}

		private static bool IsSameHeightHeld()
		{
			return PluginConfig.PieceRepeaterLevelByDefault.Value
				|| Input.GetKey(PluginConfig.PieceRepeaterLevelModifier.Value);
		}

		private void SetLine(LineRenderer line, IList<Vector3> points, bool loop)
		{
			if (!line) return;
			if (points == null || points.Count < 2)
			{
				line.enabled = false;
				return;
			}
			line.positionCount = points.Count;
			for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i] + Vector3.up * 0.06f);
			line.loop = loop;
			line.enabled = true;
		}

		private void UpdatePieceGhosts()
		{
			GameObject prefab = ZNetScene.instance && !string.IsNullOrEmpty(_prefabId)
				? ZNetScene.instance.GetPrefab(_prefabId)
				: null;
			if (!prefab || !_hasAim)
			{
				SetPieceGhostCount(0);
				return;
			}

			if (_pieceGhostPrefabId != _prefabId)
			{
				DestroyPieceGhosts();
				_pieceGhostPrefabId = _prefabId;
			}

			var visiblePlacements = new List<Placement>();
			if (_placements.Count > 0)
			{
				int count = Mathf.Min(_placements.Count, MaximumPreviewDots);
				for (int i = 0; i < count; i++) visiblePlacements.Add(_placements[i]);
			}
			else if (_active)
			{
				if (_points.Count > 0 && _hasLockedStartPlacement)
				{
					// With only the first waypoint set, keep showing its committed pose.
					// Never fall back to a cursor-following ghost after the click.
					visiblePlacements.Add(_lockedStartPlacement);
				}
				else
				{
					ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
					Placement initial = GetInitialPlacement(prefab, _aim, metadata);
					if (TryGetInitialSnapPlacement(Player.m_localPlayer, _aim, out Placement snappedPose))
						initial = snappedPose;
					visiblePlacements.Add(initial);
				}
			}

			while (_pieceGhosts.Count < visiblePlacements.Count)
				_pieceGhosts.Add(CreatePieceGhost(prefab));
			SetPieceGhostCount(visiblePlacements.Count);
			for (int i = 0; i < visiblePlacements.Count; i++)
			{
				Transform ghost = _pieceGhosts[i].transform;
				ghost.SetPositionAndRotation(visiblePlacements[i].Position, visiblePlacements[i].Rotation);
			}
		}

		private GameObject CreatePieceGhost(GameObject prefab)
		{
			var ghost = new GameObject("PieceRepeaterPieceGhost") { hideFlags = HideFlags.HideAndDontSave };
			ghost.transform.SetParent(_previewRoot.transform, false);
			ghost.SetActive(false);
			int pieceLayer = LayerMask.NameToLayer("piece");
			foreach (Transform child in prefab.transform)
			{
				if (!child.gameObject.activeSelf) continue;
				GameObject visual = UnityEngine.Object.Instantiate(child.gameObject, ghost.transform);
				visual.transform.localPosition = child.localPosition;
				visual.transform.localRotation = child.localRotation;
				visual.transform.localScale = child.localScale;
				SetLayerRecursively(visual.transform, pieceLayer);
			}

			foreach (Component component in ghost.GetComponentsInChildren<Component>(true))
				if (!(component is Renderer) && !(component is MeshFilter) && !(component is Transform))
					UnityEngine.Object.Destroy(component);

			foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer is ParticleSystemRenderer || string.Equals(renderer.name, "largelod", StringComparison.OrdinalIgnoreCase))
				{
					renderer.enabled = false;
					continue;
				}
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				Material[] materials = renderer.sharedMaterials;
				for (int i = 0; i < materials.Length; i++) materials[i] = _pieceGhostMaterial;
				renderer.sharedMaterials = materials;
			}
			ghost.SetActive(true);
			return ghost;
		}

		private static void SetLayerRecursively(Transform transform, int layer)
		{
			if (layer >= 0) transform.gameObject.layer = layer;
			for (int i = 0; i < transform.childCount; i++) SetLayerRecursively(transform.GetChild(i), layer);
		}

		private void SetPieceGhostCount(int count)
		{
			for (int i = 0; i < _pieceGhosts.Count; i++)
				if (_pieceGhosts[i]) _pieceGhosts[i].SetActive(i < count);
		}

		private void DestroyPieceGhosts()
		{
			for (int i = 0; i < _pieceGhosts.Count; i++)
				if (_pieceGhosts[i]) UnityEngine.Object.Destroy(_pieceGhosts[i]);
			_pieceGhosts.Clear();
		}

		private void ClearPreview()
		{
			ClearSnapProbe();
			if (_previewRoot) UnityEngine.Object.Destroy(_previewRoot);
			if (_previewMaterial) UnityEngine.Object.Destroy(_previewMaterial);
			if (_pieceGhostMaterial) UnityEngine.Object.Destroy(_pieceGhostMaterial);
			_previewRoot = null;
			_outlineLine = null;
			_secondLine = null;
			_previewMaterial = null;
			_pieceGhostMaterial = null;
			_pieceGhostPrefabId = null;
			_pieceGhosts.Clear();
		}

		private static void Notify(string text)
		{
			if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, text, 0, null);
		}
	}
}


