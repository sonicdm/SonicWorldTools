using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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

	internal enum ShapeFillMode
	{
		Outline,
		Grid,
		Concentric
	}

	/// <summary>IH-opened prefab line tool with metadata-driven repeat-axis selection.</summary>
	internal sealed class PieceRepeaterController : IToolSession
	{
		private const string SessionKey = "line-tool";
		string IToolSession.SessionId => SessionKey;
		bool IToolSession.IsSessionActive => _active;
		void IToolSession.StopSession(string reason) => Deactivate(reason);
		bool IToolSession.ShouldSuppressPlayerAction(ToolPlayerAction action, Player player)
		{
			if (player != Player.m_localPlayer || !IsActive) return false;
			if (action == ToolPlayerAction.Remove)
			{
				bool placementToolSelected = ToolRuntime.TryGetSelectedToolName(out string selectedTool)
				                             && ToolInstaller.IsManagedPlacementToolName(selectedTool);
				bool ownsSampleInput = placementToolSelected
				                       && ToolRuntime.InBuildMode
				                       && !ToolRuntime.TextInputBlocked
				                       && PluginConfig.PieceRepeaterSampleKey.Value == KeyCode.Mouse2
				                       && (ToolInput.WasMouseButtonDown(2) || ToolInput.WasMouseButtonHeld(2));
				if (ownsSampleInput)
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
						"[PlacementInput] Suppressed vanilla RemovePiece for placement-tool MMB sample; frame="
						+ Time.frameCount + " IHtool='" + selectedTool + "'.");
				return ownsSampleInput;
			}

			if (!OwnsPlayerInput) return false;
			return action == ToolPlayerAction.BuildPlacement
			       || action == ToolPlayerAction.Attack
			       || action == ToolPlayerAction.Interact;
		}
		private const int MaximumPlacements = 512;
		private const int MaximumPreviewDots = MaximumPlacements;
		private const float MinimumSpacing = 0.1f;
		private const float MaximumTerrainSampleDelta = 8f;
		private const float MaximumDownwardFillDepth = 64f;
		private const float ShapeRingSpacingWheelStep = 0.1f;
		private const string ShapeDescriptionMarker = "\nCurrent shape: ";
		private static readonly MethodInfo SetupPieceInfoMethod = typeof(Hud).GetMethod(
			"SetupPieceInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
			null, new[] { typeof(Piece) }, null);
		private const float RotationStepDegrees = 22.5f;
		private static PieceRepeaterController _instance;

		private readonly List<Vector3> _points = new List<Vector3>();
		private readonly List<Placement> _placements = new List<Placement>();
		private readonly List<PlacementVerification> _placementVerifications = new List<PlacementVerification>();
		private readonly List<Vector3> _outline = new List<Vector3>();
		private readonly RaycastHit[] _surfaceRayHits = new RaycastHit[64];
		private string _prefabId;
		private PieceRepeaterMode _mode;
		private ShapeFillMode _shapeFillMode;
		private float _shapeRingSpacingAdjustment;
		private bool _shapeLayoutOverLimit;
		private Piece _shapeDescriptionPiece;
		private string _shapeDescriptionValue;
		private int _shapeDescriptionLastRefreshFrame = -100;
		private bool _bothSides;
		private bool _hasLockedStartPlacement;
		private Placement _lockedStartPlacement;
		private bool _hasLockedStartAnchor;
		private bool _lockedStartAnchorIsSnap;
		private Vector3 _lockedStartAnchorWorld;
		private Vector3 _lockedStartAnchorLocal;
		private int _snapPointIndex = -1;
		private bool _active;
		private bool _shapeToolMode;
		private bool _suspendedWhileHolstered;
		private bool _suspendedForPieceMenu;
		private int _lastSettingsHotkeyFrame = -1;
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
		private int _lastPathCenterPivotCount;
		private int _lastPathInsidePivotCount;
		private float _lastPathInsideEdgeErrorMax;
		private int _catalogRevision = -1;
		private readonly PieceRepeaterSettingsPanel _settingsPanel;
		private readonly PieceRepeaterOffsetEditor _offsetEditor;

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

		private sealed class PlacementVerification
		{
			internal GameObject Instance;
			internal string PrefabId;
			internal Vector3 RequestedPosition;
			internal Quaternion RequestedRotation;
			internal float VerifyAfter;
		}

		internal static bool IsActive => _instance != null && _instance._active;
		private string ToolDisplayName => _shapeToolMode ? ToolInstaller.ShapeToolName : ToolInstaller.PieceRepeaterToolName;
		private bool AnarchyModeActive => !_shapeToolMode && PluginConfig.PieceRepeaterAnarchyMode.Value;
		private static string PlaceChordLabel => PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterPlaceModifier.Value)
			+ "+" + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterWaypointKey.Value);
		internal static bool OwnsPlayerInput => IsActive && Player.m_localPlayer && ToolRuntime.InBuildMode && ToolRuntime.InfinityHammerEquipped;
		internal static PieceRepeaterController Instance => _instance;
		internal static void RefreshKeyHints()
		{
			if (_instance?._keyHints == null) return;
			KeyHintManager.Instance.RemoveKeyHint(_instance._keyHints);
			_instance._keyHints = null;
		}
		internal static bool OwnsMiddleClick(Player player)
		{
			if (!player || player != Player.m_localPlayer) return false;
			if (PluginConfig.PieceRepeaterSampleKey.Value != KeyCode.Mouse2) return false;
			if (ToolRuntime.TextInputBlocked || !ToolInput.WasMouseButtonHeld(2)) return false;
			if (IsActive) return OwnsPlayerInput;
			if (!ToolRuntime.TryGetSelectedToolName(out string selectedTool)
			    || !ToolInstaller.IsManagedPlacementToolName(selectedTool))
			{
				return false;
			}

			CaptureAndActivate();
			return IsActive;
		}
		internal static bool ShouldSuppressPauseMenu => false;

		// Player.Update's prefix captures the settings key before vanilla build actions.
		// Tick also calls this for frames where the player update did not run first.
		private bool HandleSettingsHotkey()
		{
			PieceRepeaterController controller = this;
			if (!controller._active || !Player.m_localPlayer) return false;
			KeyCode key = PluginConfig.PieceRepeaterSettingsHotkey.Value;
			if (key == KeyCode.None || key == KeyCode.Escape || !ToolInput.WasKeyDown(key)) return false;
			// Player.Update's prefix and the plugin Tick are both fallbacks for this
			// binding. Treat the press as one action even if both run in the same frame.
			if (controller._lastSettingsHotkeyFrame == Time.frameCount) return true;
			controller._lastSettingsHotkeyFrame = Time.frameCount;
			KeyCode editorModifier = PluginConfig.PieceRepeaterOffsetEditorModifier.Value;
			if (editorModifier != KeyCode.None && ToolInput.WasModifierHeld(editorModifier))
			{
				if (controller._offsetEditor.IsOpen)
				{
					controller._offsetEditor.Close(true);
					return true;
				}
				if (controller._settingsPanel.IsOpen) return false;
				if (!OwnsPlayerInput || ToolRuntime.TextInputBlocked) return false;
				controller._offsetEditor.Open(controller._prefabId);
				return controller._offsetEditor.IsOpen;
			}
			if (controller._offsetEditor.IsOpen)
			{
				controller._offsetEditor.Close(true);
				return true;
			}
			if (controller._settingsPanel.IsOpen)
			{
				controller._settingsPanel.Close(true);
				return true;
			}
			if (!OwnsPlayerInput) return false;
			if (ToolRuntime.TextInputBlocked) return false;
			controller._settingsPanel.Open();
			return controller._settingsPanel.IsOpen;
		}

		internal static bool TryConsumeSettingsHotkey()
		{
			return _instance != null && _instance.HandleSettingsHotkey();
		}

		internal PieceRepeaterController()
		{
			_instance = this;
			_settingsPanel = new PieceRepeaterSettingsPanel(this);
			_offsetEditor = new PieceRepeaterOffsetEditor(this);
		}

		internal string SelectedPrefabId => _prefabId;
		internal void RefreshOffsetPreview()
		{
			RefreshLockedStartForCurrentProfile();
			UpdatePreview();
		}
		internal void OffsetEditorClosed()
		{
			_awaitPrimaryRelease = true;
			RefreshLockedStartForCurrentProfile();
			UpdatePreview();
		}

		private void RefreshPreviewForCatalogChanges()
		{
			int revision = PrefabPlacementMetadata.CatalogRevision;
			if (_catalogRevision == revision) return;
			_catalogRevision = revision;
			RefreshLockedStartForCurrentProfile();
			UpdatePreview();
		}

		private void RefreshLockedStartForCurrentProfile()
		{
			if (!_active || !_hasLockedStartPlacement || !_hasLockedStartAnchor
			    || !ZNetScene.instance || string.IsNullOrWhiteSpace(_prefabId)) return;
			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			if (!prefab) return;
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			if (metadata.ProfileType == RepeatProfileType.EdgeChain && _points.Count > 1)
			{
				// A profile/offset edit re-anchors the start using the first committed
				// leg, then lets the normal full-route solve update every following ghost.
				UpdateLockedStartForDirection(prefab, metadata, _points[1]);
				return;
			}

			// Keep the vanilla snap anchor fixed while applying live local corrections.
			Quaternion rotation = _lockedStartPlacement.Rotation;
			Vector3 root = _lockedStartAnchorWorld - rotation * _lockedStartAnchorLocal
				+ rotation * metadata.PositionOffset;
			_lockedStartPlacement = new Placement(root, rotation);
			if (_points.Count > 0) _points[0] = root + rotation * metadata.CenterOffset;
		}

		internal static void CaptureAndActivate()
		{
			if (_instance == null || _instance._active)
			{
				return;
			}

			// IH may not expose the underlying selected build piece when the tool is
			// re-equipped. Keep the last sampled prefab unless a new valid selection
			// was actually captured; otherwise holster/re-equip silently makes the tool
			// look uninitialized and it behaves like the vanilla hammer.
			if (InfinityHammerBridge.TryGetSelectedBuildPrefabId(out string prefabId))
			{
				_instance._prefabId = prefabId;
			}
			_instance.Activate();
		}

		internal void Activate()
		{
			if (!ToolSessionCoordinator.Acquire(this)) return;
			if (InfinityHammerBridge.TrySuspendShapeShortcut(out IDisposable restoreShapeShortcut))
				_restoreIhShapeShortcut = restoreShapeShortcut;
			_active = true;
			_suspendedWhileHolstered = false;
			_suspendedForPieceMenu = false;
			_shapeToolMode = ToolRuntime.TryGetSelectedToolName(out string selectedTool)
			                 && ToolInstaller.IsShapeToolName(selectedTool);
			_mode = _shapeToolMode ? PieceRepeaterMode.Circle : PieceRepeaterMode.Path;
			_shapeFillMode = ShapeFillMode.Outline;
			_shapeRingSpacingAdjustment = 0f;
			_bothSides = false;
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			_snapPointIndex = -1;
			_gapMeters = Mathf.Clamp(PluginConfig.PieceRepeaterGap.Value, -5f, 5f);
			_rotationSteps = 0;
			_sideOffset = 2f;
			_points.Clear();
			_lastPathDiagnosticKey = null;
			_lastPathDiagnosticPointCount = -1;
			_awaitPrimaryRelease = true;
			EnsurePreview();
			UpdatePreview();
			Notify(_prefabId == null
				? ToolDisplayName + ": " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSampleKey.Value) + " samples a placed build piece."
				: ToolDisplayName + " ready: " + _prefabId + (_shapeToolMode
					? ". Click a center, move to size; Q cycles shapes; " + PlaceChordLabel + " places the outline."
					: ". Settings: " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSettingsHotkey.Value)
					  + "; " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterWaypointKey.Value) + " adds waypoints."));
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[" + ToolDisplayName + "] Activated; prefab=" + (_prefabId ?? "<sample required>"));
		}

		internal void Deactivate(string reason = "cancel")
		{
			if (!_active)
			{
				return;
			}

			_active = false;
			ToolSessionCoordinator.Release(this);
			_suspendedWhileHolstered = false;
			_suspendedForPieceMenu = false;
			_settingsPanel.Close(false);
			_offsetEditor.Close(false);
			if (_keyHints != null) KeyHintManager.Instance.RemoveKeyHint(_keyHints);
			_keyHints = null;
			_restoreIhShapeShortcut?.Dispose();
			_restoreIhShapeShortcut = null;
			_points.Clear();
			_lastPathDiagnosticKey = null;
			_lastPathDiagnosticPointCount = -1;
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			_snapPointIndex = -1;
			_placements.Clear();
			_outline.Clear();
			_awaitPrimaryRelease = false;
			ClearPreview();
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Deactivated: " + reason + ".");
		}

		private void CancelPendingPlacement(string reason)
		{
			bool hadPendingShape = _shapeToolMode && _points.Count > 0;
			_points.Clear();
			_placements.Clear();
			_outline.Clear();
			_hasAim = false;
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			_awaitPrimaryRelease = true;
			UpdatePreview();
			if (hadPendingShape)
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[Shape Tool] Cancelled pending outline: " + reason + ".");
		}

		internal void Shutdown()
		{
			Deactivate("plugin-destroy");
			_settingsPanel.Shutdown();
			_offsetEditor.Shutdown();
			ClearPreview();
			if (_instance == this) _instance = null;
		}

		internal static void OnOtherBuildPieceSelected(Piece piece)
		{
			if (IsActive && piece
			    && !ToolIdentityTracker.IsOurPiece(piece)
				&& !ToolIdentityTracker.IsManagedPlacementToolPiece(piece))
			{
				_instance.Deactivate("other-build-piece");
			}
		}

		internal void Tick()
		{
			VerifyRecentPlacements();
			if (!_active && ToolRuntime.LocalPlayer && ToolRuntime.InBuildMode
			    && ToolRuntime.TryGetSelectedToolName(out string armingTool)
			    && ToolInstaller.IsManagedPlacementToolName(armingTool))
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
				// Holstering the hammer temporarily drops InPlaceMode. IH does not
				// always re-emit its tool-selection command when the hammer is drawn
				// again, so retain this tool's state and resume it on the next build
				// frame. Do not own inputs while suspended (OwnsPlayerInput checks
				// InPlaceMode), and hide ghosts/key hints to avoid stale world visuals.
				if (!_suspendedWhileHolstered)
				{
					_suspendedWhileHolstered = true;
					if (_shapeToolMode) CancelPendingPlacement("holstered");
					_settingsPanel.Close(false);
					_offsetEditor.Close(false);
					if (_keyHints != null) KeyHintManager.Instance.RemoveKeyHint(_keyHints);
					_keyHints = null;
					_restoreIhShapeShortcut?.Dispose();
					_restoreIhShapeShortcut = null;
					if (_previewRoot) _previewRoot.SetActive(false);
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
						"[Line Tool] Suspended while holstered; retaining prefab="
						+ (_prefabId ?? "<none>") + " waypoints=" + _points.Count + ".");
				}
				return;
			}

			if (!ToolRuntime.InfinityHammerEquipped)
			{
				Deactivate("infinity-hammer-not-equipped");
				return;
			}

			if (_suspendedWhileHolstered)
			{
				_suspendedWhileHolstered = false;
				if (InfinityHammerBridge.TrySuspendShapeShortcut(out IDisposable restoreShapeShortcut))
					_restoreIhShapeShortcut = restoreShapeShortcut;
				if (_previewRoot) _previewRoot.SetActive(true);
				_hasAim = false;
				UpdatePreview();
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[Line Tool] Resumed after holster; prefab="
					+ (_prefabId ?? "<none>") + " waypoints=" + _points.Count + ".");
			}

			// IH owns tool selection. A managed Line Tool must stay selected in IH;
			// switching to another registered tool ends this session.
			bool hasSelectedTool = ToolRuntime.TryGetSelectedToolName(out string selectedTool);
			if (!hasSelectedTool || !ToolInstaller.IsManagedPlacementToolName(selectedTool))
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[LineLifecycle] IH " + (hasSelectedTool ? "selected '" + selectedTool + "'" : "cleared its tool selection")
				+ " while Line Tool was active; ending session.");
				Deactivate(hasSelectedTool ? "other-ih-tool" : "ih-tool-selection-cleared");
				return;
			}

			bool shapeToolSelected = ToolInstaller.IsShapeToolName(selectedTool);
			if (shapeToolSelected != _shapeToolMode)
			{
				_shapeToolMode = shapeToolSelected;
				_mode = _shapeToolMode ? PieceRepeaterMode.Circle : PieceRepeaterMode.Path;
				_shapeFillMode = ShapeFillMode.Outline;
				_shapeRingSpacingAdjustment = 0f;
				_bothSides = false;
				_points.Clear();
				_hasLockedStartPlacement = false;
				_hasLockedStartAnchor = false;
				_lockedStartAnchorIsSnap = false;
				_rotationSteps = 0;
				_awaitPrimaryRelease = true;
				if (_keyHints != null) KeyHintManager.Instance.RemoveKeyHint(_keyHints);
				_keyHints = null;
				UpdatePreview();
				Notify(_shapeToolMode
					? "Shape Tool ready: click a center, move to size the outline; Q cycles circle, square, rectangle."
					: "Line Tool ready: click waypoints; " + PlaceChordLabel + " finishes and places the route.");
			}

			bool pieceMenuVisible = false;
			try { pieceMenuVisible = Hud.IsPieceSelectionVisible(); }
			catch { /* HUD may not be initialized during load or logout. */ }
			if (_shapeToolMode && pieceMenuVisible)
			{
				if (!_suspendedForPieceMenu)
				{
					_suspendedForPieceMenu = true;
					CancelPendingPlacement("piece-menu-open");
					if (_keyHints != null) KeyHintManager.Instance.RemoveKeyHint(_keyHints);
					_keyHints = null;
					_restoreIhShapeShortcut?.Dispose();
					_restoreIhShapeShortcut = null;
					if (_previewRoot) _previewRoot.SetActive(false);
					SonicWorldToolsPlugin.ModLogger?.LogInfo("[Shape Tool] Cancelled pending outline when the piece menu opened.");
				}
				return;
			}
			if (_suspendedForPieceMenu)
			{
				_suspendedForPieceMenu = false;
				if (InfinityHammerBridge.TrySuspendShapeShortcut(out IDisposable restoreShapeShortcut))
					_restoreIhShapeShortcut = restoreShapeShortcut;
				if (_previewRoot) _previewRoot.SetActive(true);
				_hasAim = false;
				UpdatePreview();
			}

			if (ToolInput.WasKeyDown(KeyCode.Escape))
			{
				bool panelWasOpen = _settingsPanel.IsOpen || _offsetEditor.IsOpen;
				Deactivate("escape");
				// A modal Jotunn panel blocks Valheim's normal Escape handler. Restore
				// the expected tool-exit-and-menu behavior on this same keypress.
				if (panelWasOpen && Menu.instance) Menu.instance.Show();
				return;
			}
			// Player.Update can be skipped or reordered by IH/ScriptEngine tool
			// selection. Poll here too; the frame guard prevents a prefix+Tick press
			// from opening and immediately closing the modal.
			if (HandleSettingsHotkey()) return;
			if (_settingsPanel.IsOpen)
			{
				return;
			}
			RefreshPreviewForCatalogChanges();
			if (ToolRuntime.TextInputBlocked) return;
			if (_shapeToolMode && ToolInput.WasKeyDown(KeyCode.I))
			{
				if (_mode == PieceRepeaterMode.Circle)
					_shapeFillMode = _shapeFillMode == ShapeFillMode.Outline ? ShapeFillMode.Concentric : ShapeFillMode.Outline;
				else
					_shapeFillMode = _shapeFillMode == ShapeFillMode.Outline ? ShapeFillMode.Grid
						: _shapeFillMode == ShapeFillMode.Grid ? ShapeFillMode.Concentric : ShapeFillMode.Outline;
				_awaitPrimaryRelease = true;
				UpdatePreview();
				Notify("Shape Tool: " + GetShapeFillLabel() + ".");
				return;
			}
			if (_shapeToolMode && ToolInput.WasKeyDown(KeyCode.Q))
			{
				_mode = _mode == PieceRepeaterMode.Circle ? PieceRepeaterMode.Square
					: _mode == PieceRepeaterMode.Square ? PieceRepeaterMode.Rectangle
					: PieceRepeaterMode.Circle;
				_awaitPrimaryRelease = true;
				UpdatePreview();
				Notify(_points.Count == 0
					? "Shape Tool: " + GetShapeFillLabel() + " " + _mode.ToString().ToLowerInvariant() + ". Click a center; square direction sets angle."
					: "Shape Tool: " + GetShapeFillLabel() + " " + _mode.ToString().ToLowerInvariant() + " preview updated.");
				return;
			}
			HandleSnapPointInput(player);
			EnsureKeyHints(player);
			float wheel = ToolInput.MouseScrollY;
			if (Mathf.Abs(wheel) > 0.01f)
			{
				if (_shapeToolMode && _mode != PieceRepeaterMode.Circle
				    && _shapeFillMode == ShapeFillMode.Concentric
				    && ToolInput.WasModifierHeld(KeyCode.LeftShift))
				{
					if (ZNetScene.instance && !string.IsNullOrEmpty(_prefabId)
					    && ZNetScene.instance.GetPrefab(_prefabId) is GameObject shapePrefab)
					{
						ResolvedPrefabPlacement shapeMetadata = ResolvePlacementMetadata(shapePrefab);
						float baseSpacing = GetShapeRingSpacingBase(shapePrefab, shapeMetadata);
						float current = Mathf.Max(MinimumSpacing, baseSpacing + _shapeRingSpacingAdjustment);
						float next = Mathf.Max(MinimumSpacing,
							current + Mathf.Sign(wheel) * ShapeRingSpacingWheelStep);
						_shapeRingSpacingAdjustment = next - baseSpacing;
						Notify("Concentric ring spacing: " + next.ToString("0.##") + " m.");
					}
					else Notify("Sample a piece before adjusting concentric ring spacing.");
				}
				else if (ToolInput.WasModifierHeld(PluginConfig.PieceRepeaterSpacingModifier.Value))
				{
					SetGap(_gapMeters + wheel * 0.1f);
					Notify("Spacing offset: " + _gapMeters.ToString("0.##") + " m; repeat interval: " + GetSpacing().ToString("0.##") + " m.");
				}
				else
				{
					int rotationDelta = wheel > 0f ? 1 : -1;
					_rotationSteps = (_rotationSteps + rotationDelta + 16) % 16;
					Notify("Piece rotation: " + (_rotationSteps * RotationStepDegrees).ToString("0.#") + "°.");
				}
				UpdatePreview();
			}

			if (ToolInput.WasBoundKeyOrMouseDown(PluginConfig.PieceRepeaterSampleKey.Value))
			{
				SampleHoveredPiece(player);
				return;
			}

			_hasAim = PrecisionAreaPipetteInput.TryGetAimPoint(out _aim);
			UpdatePreview();
			if (_awaitPrimaryRelease)
			{
				if (!ToolInput.WasBoundKeyOrMouseHeld(PluginConfig.PieceRepeaterWaypointKey.Value))
				{
					_awaitPrimaryRelease = false;
				}
				return;
			}

			if (!ToolInput.WasBoundKeyOrMouseDown(PluginConfig.PieceRepeaterWaypointKey.Value) || !_hasAim)
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(_prefabId))
			{
				Notify(ToolDisplayName + ": " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSampleKey.Value) + " samples a placed building piece first.");
				return;
			}

			bool finish = ToolInput.WasModifierHeld(PluginConfig.PieceRepeaterPlaceModifier.Value);
			if (_mode == PieceRepeaterMode.Path)
			{
				List<Placement> previousPreview = null;
				Vector3 committedWaypoint = Vector3.zero;
				bool waypointAdded = false;
				GameObject selectedPrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(_prefabId) : null;
				if (!selectedPrefab || (!AnarchyModeActive && (PrefabPlacementMetadata.IsExcludedPiece(selectedPrefab)
				    || !ResolvePlacementMetadata(selectedPrefab).Enabled
				    || ResolvePlacementMetadata(selectedPrefab).Status != RepeatProfileStatus.Ready)))
				{
					Notify(ToolDisplayName + ": enable this piece and give it a Ready profile in VanillaPieces.yml, or enable Anarchy Mode with sonic_line_anarchy.");
					return;
				}
				if (_points.Count == 0 && ZNetScene.instance
				    && ZNetScene.instance.GetPrefab(_prefabId) is GameObject firstPrefab)
				{
					ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(firstPrefab);
					Placement startPlacement = AnarchyModeActive
						? GetAnarchyInitialPlacement(_aim, metadata)
						: GetInitialPlacement(firstPrefab, _aim, metadata);
					if (!AnarchyModeActive
					    && TryGetInitialSnapPlacement(player, _aim, out Placement snappedStart,
						out Vector3 snapAnchorWorld, out Vector3 snapAnchorLocal))
					{
						startPlacement = snappedStart;
						_lockedStartAnchorWorld = snapAnchorWorld;
						_lockedStartAnchorLocal = snapAnchorLocal;
						_lockedStartAnchorIsSnap = true;
					}
					else
					{
						_lockedStartAnchorLocal = metadata.CenterOffset;
						_lockedStartAnchorWorld = startPlacement.Position + startPlacement.Rotation * metadata.CenterOffset;
						_lockedStartAnchorIsSnap = false;
					}
					_hasLockedStartAnchor = true;
					_lockedStartPlacement = startPlacement;
					_rotationStepsAtStart = _rotationSteps;
					_hasLockedStartPlacement = true;
					// Waypoints are stamp centers for the path solver. The first model
					// remains at its vanilla-snapped transform, but its center is the
					// first curve point so subsequent pieces are fitted from the actual
					// centerline instead of starting one half-piece ahead at JoinEnd.
					_points.Add(startPlacement.Position + startPlacement.Rotation * metadata.CenterOffset);
				}
				else if (_points.Count == 0)
				{
					_hasLockedStartPlacement = false;
					_points.Add(_aim);
				}
				else
				{
					Vector3 waypoint = GetPathWaypoint(_aim);
					previousPreview = new List<Placement>(_placements);
					committedWaypoint = waypoint;
					// The first route leg determines the starter piece's secant heading.
					// Keep its vanilla snap target fixed while solving the new rotation.
					// Use the committed waypoint (including Right-Alt joint snapping), so
					// the initial piece and fitted route use the same endpoint.
					Vector3 fromLast = waypoint - _points[_points.Count - 1];
					fromLast.y = 0f;
					if (fromLast.sqrMagnitude < 0.0001f && !finish)
					{
						Notify("Waypoint remains at the previous joint; move farther or release the joint modifier.");
						return;
					}
					if (_points.Count == 1 && _hasLockedStartPlacement)
					{
						ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(selectedPrefab);
						if (metadata.ProfileType == RepeatProfileType.EdgeChain)
							UpdateLockedStartForDirection(selectedPrefab, metadata, waypoint);
					}
					if (fromLast.sqrMagnitude >= 0.0001f)
					{
						_points.Add(waypoint);
						waypointAdded = true;
					}
				}
				if (finish)
				{
					BuildAndPlace(player);
				}
				else
				{
					UpdatePreview();
					if (waypointAdded) LogWaypointSolve(previousPreview, committedWaypoint);
					Notify("Path point " + _points.Count + "; " + PlaceChordLabel + " to finish and place.");
				}
				return;
			}

			if (_points.Count == 0)
			{
				_points.Add(_aim);
				UpdatePreview();
				Notify(_shapeToolMode
					? _mode + ": set size with the cursor; square direction also sets its angle. I cycles "
					  + (_mode == PieceRepeaterMode.Circle ? "outline and concentric infill" : "outline, grid and concentric infill")
					  + (_mode == PieceRepeaterMode.Circle ? ". " : "; Shift+wheel changes ring spacing.")
					  + " " + PlaceChordLabel + " places."
					: _mode + ": set size with the cursor, then " + PlaceChordLabel + " to place.");
				return;
			}

			if (finish)
			{
				BuildAndPlace(player);
			}
			else
			{
				Notify(PlaceChordLabel + " to place the previewed " + _mode.ToString().ToLowerInvariant() + ".");
			}
		}

		internal float GapMeters => _gapMeters;

		internal void SetGap(float gap)
		{
			_gapMeters = Mathf.Round(Mathf.Clamp(gap, -5f, 5f) * 10f) / 10f;
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
			if (pathPoints.Count == 0) return aim;
			Vector3 last = pathPoints[pathPoints.Count - 1];
			Vector3 point = aim;
			bool jointSnap = ToolInput.WasModifierHeld(PluginConfig.PieceRepeaterJointModifier.Value);
			if (jointSnap)
			{
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
				point = last + horizontal / length * along;
				point.y = aim.y;
			}
			else if (PluginConfig.PieceRepeaterWorldDirectionSnap.Value)
			{
				Vector3 horizontal = point - last;
				horizontal.y = 0f;
				float length = horizontal.magnitude;
				if (length < 0.001f) return last;
				float angle = Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg;
				float snappedAngle = Mathf.Round(angle / RotationStepDegrees) * RotationStepDegrees;
				float radians = snappedAngle * Mathf.Deg2Rad;
				point.x = last.x + Mathf.Sin(radians) * length;
				point.z = last.z + Mathf.Cos(radians) * length;
			}

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
					new ButtonConfig { Name = "Attack", Key = PluginConfig.PieceRepeaterWaypointKey.Value, Hint = _shapeToolMode ? "Set center / " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterPlaceModifier.Value) + ": Place outline" : "Waypoint / " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterPlaceModifier.Value) + ": Place" },
					new ButtonConfig { Name = "Remove", Key = PluginConfig.PieceRepeaterSampleKey.Value, Hint = "Sample" },
					new ButtonConfig { Name = "Mouse ScrollWheel", Axis = "Mouse ScrollWheel", Hint = _shapeToolMode ? "Rotate / square or rectangle concentric: Shift+wheel spacing / " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSpacingModifier.Value) + "+wheel: gap" : "Rotate pieces / " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSpacingModifier.Value) + "+wheel: gap" },
					new ButtonConfig { Name = "TabLeft", Key = KeyCode.Q, Hint = _shapeToolMode ? "Cycle shape (Q)" : "Cycle snap points (Q/E)" },
					new ButtonConfig { Name = "TabRight", Key = KeyCode.E, Hint = _shapeToolMode ? "Cycle snap points (E)" : "Cycle snap points (Q/E)" },
					new ButtonConfig { Name = "Run", Key = PluginConfig.PieceRepeaterLevelModifier.Value, Hint = "Level height; " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterJointModifier.Value) + "+click: joint" }
				}
			};
			if (KeyHintManager.Instance.AddKeyHint(config))
			{
				_keyHints = config;
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Registered key hints for " + config + ".");
			}
		}

		private void SampleHoveredPiece(Player player)
		{
			if (AnarchyModeActive)
			{
				SampleHoveredNetworkObject(player);
				return;
			}

			Piece hovering = player.GetHoveringPiece();
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[LineInput] MMB sample handler frame=" + Time.frameCount
				+ " buildMode=" + player.InPlaceMode()
				+ " textBlocked=" + ToolRuntime.TextInputBlocked
				+ " prefabBefore=" + (_prefabId ?? "<none>")
				+ " hover=" + (hovering ? hovering.name : "<none>"));
			if (!hovering)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[LineInput] MMB sample result: no hovered Piece.");
				Notify("Aim at a placed building piece and press " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSampleKey.Value) + " to sample it.");
				return;
			}

			ZNetView view = hovering.GetComponent<ZNetView>();
			if (!view)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[LineInput] MMB sample result: hovered Piece has no ZNetView.");
				Notify("That target is not a networked building piece.");
				return;
			}

			ZDO zdo = view.GetZDO();
			GameObject registered = zdo != null && ZNetScene.instance ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
			string prefab = registered ? Utils.GetPrefabName(registered) : Utils.GetPrefabName(view.gameObject);
			registered = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null;
			if (!registered || !registered.GetComponent<Piece>())
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[LineInput] MMB sample result: prefab is not placeable ('" + prefab + "').");
				Notify("That prefab cannot be placed as a building piece.");
				return;
			}

			_prefabId = prefab;
			_shapeRingSpacingAdjustment = 0f;
			_points.Clear();
			_hasLockedStartPlacement = false;
			_snapPointIndex = -1;
			_sampledYaw = hovering.transform.eulerAngles.y;
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[LineInput] MMB sample accepted frame=" + Time.frameCount
				+ " prefab=" + _prefabId + " yaw=" + _sampledYaw.ToString("0.##") + ".");
			Notify(ToolDisplayName + " selected " + prefab + ".");
		}

		private void SampleHoveredNetworkObject(Player player)
		{
			if (!ZNetScene.instance || !GameCamera.instance)
			{
				Notify(ToolDisplayName + ": world targeting is unavailable.");
				return;
			}

			Ray ray = new Ray(GameCamera.instance.transform.position, GameCamera.instance.transform.forward);
			RaycastHit[] hits = Physics.RaycastAll(ray, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
			for (int i = 0; i < hits.Length; i++)
			{
				Collider collider = hits[i].collider;
				if (!collider) continue;
				Transform target = collider.transform;
				Character creature = collider.GetComponentInParent<Character>();
				ZNetView view = collider.GetComponentInParent<ZNetView>();
				if (creature && creature != player)
				{
					target = creature.transform;
					if (!view) view = creature.GetComponent<ZNetView>();
				}
				if (!view || target == player.transform || target.IsChildOf(player.transform)) continue;

				ZDO zdo = view.GetZDO();
				GameObject registered = zdo != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
				if (!registered)
				{
					string candidateName = Utils.GetPrefabName(view.gameObject);
					registered = ZNetScene.instance.GetPrefab(candidateName);
				}
				if (!registered || !registered.GetComponent<ZNetView>()) continue;

				_prefabId = Utils.GetPrefabName(registered);
				_shapeRingSpacingAdjustment = 0f;
				_points.Clear();
				_hasLockedStartPlacement = false;
				_hasLockedStartAnchor = false;
				_snapPointIndex = -1;
				_sampledYaw = target.eulerAngles.y;
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[LineInput] Anarchy sample accepted frame=" + Time.frameCount
					+ " prefab=" + _prefabId + " target=" + target.name + " type="
					+ (creature && creature != player ? "Character" : "ZNetView") + " yaw=" + _sampledYaw.ToString("0.##") + ".");
				Notify(ToolDisplayName + " anarchy selected " + _prefabId + ".");
				return;
			}

			Notify("Anarchy sample: aim at a registered networked world object, such as a tree or creature.");
		}

		private void BuildAndPlace(Player player)
		{
			if (!ZNetScene.instance)
			{
				Notify(ToolDisplayName + ": prefab scene is unavailable.");
				_points.Clear();
				return;
			}

			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			bool anarchy = AnarchyModeActive;
			if (!prefab || (!piece && !anarchy) || (anarchy && !prefab.GetComponent<ZNetView>()))
			{
				Notify(ToolDisplayName + ": selected prefab is unavailable or is not a networked spawnable object.");
				_prefabId = null;
				_points.Clear();
				return;
			}

			GenerateLayout(prefab);
			if (_shapeLayoutOverLimit)
			{
				Notify("Shape Tool: concentric infill exceeds the safe limit of "
					+ MaximumPlacements + " placements. Increase ring spacing or reduce the shape.");
				return;
			}
			if (_placements.Count == 0)
			{
				Notify(ToolDisplayName + ": add more points or increase the size.");
				_points.Clear();
				return;
			}
			if (_placements.Count > MaximumPlacements)
			{
				Notify(ToolDisplayName + ": " + _placements.Count + " placements exceeds the safe limit of " + MaximumPlacements + ". Increase spacing or reduce the shape.");
				_points.Clear();
				return;
			}

			int requests = 0;
			int matchedInstances = 0;
			ResolvedPrefabPlacement batchMetadata = ResolvePlacementMetadata(prefab);
			PieceRepeaterDiagnostics.Log("BatchBegin", "prefab=" + _prefabId + " mode=" + _mode
				+ " waypoints=" + _points.Count + " placements=" + _placements.Count
				+ " profile=" + batchMetadata.ProfileType + "/" + batchMetadata.Status + " axis=" + batchMetadata.Axis
				+ " joins=" + batchMetadata.JoinStart.ToString("F3") + "->" + batchMetadata.JoinEnd.ToString("F3")
				+ " positionOffset=" + batchMetadata.PositionOffset.ToString("F3")
				+ " yawOffset=" + batchMetadata.YawOffset.ToString("0.###") + " bottomOffset=" + batchMetadata.BottomOffset.ToString("0.###")
				+ " gap=" + _gapMeters.ToString("0.###") + " smoothing=" + PluginConfig.PieceRepeaterCornerRadius.Value.ToString("0.##"));
			IDisposable restoreIhPlacement = null;
			if (!anarchy && !InfinityHammerBridge.TrySuspendPlacementInterception(out restoreIhPlacement))
			{
				Notify(ToolDisplayName + ": could not safely bypass IH's active tool placement hook; no pieces placed.");
				_points.Clear();
				UpdatePreview();
				return;
			}

			if (!anarchy) WorldEditUndoBridge.BeginAction();
			using (ToolActionGate.BeginTrustedPlacement())
			{
				try
				{
					using (restoreIhPlacement)
					{
						for (int index = 0; index < _placements.Count; index++)
						{
							Placement placement = _placements[index];
							if (anarchy)
							{
								ZNetScene.instance.SpawnObject(placement.Position, placement.Rotation, prefab);
								requests++;
								PieceRepeaterDiagnostics.Log("SpawnRequest", "index=" + index + " prefab=" + _prefabId
									+ " position=" + placement.Position.ToString("F3")
									+ " yaw=" + placement.Rotation.eulerAngles.y.ToString("0.0") + " networkSpawn=true");
								continue;
							}

							HashSet<int> before = GetWearNTearInstanceIds();
							player.PlacePiece(piece, placement.Position, placement.Rotation, true);
							requests++;
							List<WearNTear> created = FindNewMatchingInstances(before, _prefabId, placement);
							if (created.Count > 0) matchedInstances++;
							float previousJoinError = index > 0 ? GetJoinError(_placements[index - 1], placement, prefab) : 0f;
							PieceRepeaterDiagnostics.Log("PlaceRequest", "index=" + index + " prefab=" + _prefabId
								+ " root=" + placement.Position.ToString("F3") + " yaw=" + placement.Rotation.eulerAngles.y.ToString("0.0")
								+ " previousJoinError=" + previousJoinError.ToString("0.000") + "m"
								+ " matchingWearNTearCreated=" + created.Count
								+ (created.Count > 0 ? " instance=" + created[0].name + " id=" + created[0].GetInstanceID() : string.Empty));
							for (int createdIndex = 0; createdIndex < created.Count; createdIndex++)
								_placementVerifications.Add(new PlacementVerification
								{
									Instance = created[createdIndex].gameObject,
									PrefabId = _prefabId,
									RequestedPosition = placement.Position,
									RequestedRotation = placement.Rotation,
									VerifyAfter = Time.unscaledTime + 2f
								});
						}
					}
				}
				catch (Exception ex)
				{
					PieceRepeaterDiagnostics.Log("BatchException", "requests=" + requests + " error=" + ex);
					SonicWorldToolsPlugin.ModLogger?.LogWarning("[Line Tool] Placement stopped after " + requests + " requests: " + ex.Message);
				}
				finally
				{
					if (!anarchy) WorldEditUndoBridge.EndAction();
				}
			}

			PieceRepeaterDiagnostics.Log("BatchEnd", "prefab=" + _prefabId + " requests=" + requests
				+ " withImmediateInstance=" + matchedInstances + " pendingDelayedVerification=" + _placementVerifications.Count);
			Notify(ToolDisplayName + " sent " + requests + " " + _prefabId + (anarchy
				? " network spawn request(s). Confirm the objects appear before placing another batch."
				: " placement request(s); detected " + matchedInstances
				  + " structural instances immediately. Use IH undo to reverse the batch.")
				+ (PluginConfig.LineToolDiagnostics.Value ? " Diagnostics: BepInEx/logs/SonicWorldTools-LineTool.log." : ""));
			_points.Clear();
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
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
			Vector3 correction = rotation * metadata.PositionOffset;
			Vector3 rootPosition = placement.Position - rotation * metadata.CenterOffset + correction;
			rootPosition = ApplySurfaceHeightMode(rootPosition, groundOffset, fixedHeight);
			rootPosition.y += correction.y;
			return new Placement(rootPosition, rotation);
		}

		private void GenerateLayoutCore(GameObject prefab)
		{
			_placements.Clear();
			_outline.Clear();
			_shapeLayoutOverLimit = false;
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
				if (_shapeFillMode != ShapeFillMode.Outline) GenerateCircleFill(a, radius, spacing);
				else GenerateCircleOutline(a, radius, spacing);
				return;
			}

			float dx = _aim.x - a.x;
			float dz = _aim.z - a.z;
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			if (_mode == PieceRepeaterMode.Square)
			{
				float radius = Mathf.Sqrt(dx * dx + dz * dz);
				float requestedSide = radius * Mathf.Sqrt(2f);
				float side = Mathf.Max(spacing, Mathf.Round(requestedSide / spacing) * spacing);
				float half = side * 0.5f;
				if (half < MinimumSpacing * 0.5f) return;
				Vector3 diagonal = new Vector3(dx, 0f, dz).normalized;
				Vector3 perpendicular = new Vector3(-diagonal.z, 0f, diagonal.x);
				Vector3 axisX = (diagonal + perpendicular).normalized;
				Vector3 axisZ = (diagonal - perpendicular).normalized;
				Vector3[] corners =
				{
					a - axisX * half - axisZ * half,
					a + axisX * half - axisZ * half,
					a + axisX * half + axisZ * half,
					a - axisX * half + axisZ * half
				};
				if (_shapeFillMode == ShapeFillMode.Grid) GenerateFilledRectangle(a, axisX, axisZ, half, half, spacing, metadata);
				else if (_shapeFillMode == ShapeFillMode.Concentric)
					GenerateConcentricRectangle(a, axisX, axisZ, half, half, spacing,
						GetShapeRingSpacing(prefab, metadata), prefab, metadata);
				else GenerateOutlineEdges(corners, spacing, prefab, metadata);
				_outline.AddRange(corners);
			}
			else
			{
				if (Mathf.Abs(dx) < MinimumSpacing * 0.5f || Mathf.Abs(dz) < MinimumSpacing * 0.5f) return;
				GenerateRectangle(a, _aim, spacing, prefab, metadata);
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
			// Build radial rows instead of a staggered Cartesian/hex grid so the
			// infill follows the same circular form as its boundary.
			_placements.Add(new Placement(center, Quaternion.identity));
			float ringStep = spacing * 0.8660254f;
			int ringCount = Mathf.CeilToInt(radius / ringStep);
			for (int ring = 1; ring <= ringCount; ring++)
			{
				float ringRadius = Mathf.Min(radius, ring * ringStep);
				int count = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * ringRadius / spacing));
				float angularOffset = (ring & 1) == 0 ? Mathf.PI / count : 0f;
				for (int i = 0; i < count; i++)
				{
					float angle = angularOffset + 2f * Mathf.PI * i / count;
					Vector3 position = center + new Vector3(Mathf.Sin(angle) * ringRadius, 0f, Mathf.Cos(angle) * ringRadius);
					Quaternion tangent = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + 90f, 0f);
					_placements.Add(new Placement(position, tangent));
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

		private void GenerateRectangle(Vector3 a, Vector3 b, float spacing, GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			// Rectangles start at the first-click center, just like circles and squares.
			float halfX = Mathf.Max(spacing * 0.5f, Mathf.Round(Mathf.Abs(b.x - a.x) * 2f / spacing) * spacing * 0.5f);
			float halfZ = Mathf.Max(spacing * 0.5f, Mathf.Round(Mathf.Abs(b.z - a.z) * 2f / spacing) * spacing * 0.5f);
			float minX = a.x - halfX;
			float maxX = a.x + halfX;
			float minZ = a.z - halfZ;
			float maxZ = a.z + halfZ;

			Vector3[] corners =
			{
				new Vector3(minX, a.y, minZ), new Vector3(maxX, a.y, minZ),
				new Vector3(maxX, a.y, maxZ), new Vector3(minX, a.y, maxZ)
			};
			if (_shapeFillMode == ShapeFillMode.Grid)
			{
				GenerateFilledRectangle(a, Vector3.right, Vector3.forward, halfX, halfZ, spacing, metadata);
			}
			else if (_shapeFillMode == ShapeFillMode.Concentric)
			{
				GenerateConcentricRectangle(a, Vector3.right, Vector3.forward, halfX, halfZ, spacing,
					GetShapeRingSpacing(prefab, metadata), prefab, metadata);
			}
			else
			{
				GenerateOutlineEdges(corners, spacing, prefab, metadata);
			}
			_outline.AddRange(corners);
		}

		private void GenerateFilledRectangle(Vector3 center, Vector3 axisX, Vector3 axisZ,
			float halfX, float halfZ, float spacing, ResolvedPrefabPlacement metadata)
		{
			int countX = Mathf.Max(1, Mathf.RoundToInt(halfX * 2f / spacing));
			int countZ = Mathf.Max(1, Mathf.RoundToInt(halfZ * 2f / spacing));
			float stepX = halfX * 2f / countX;
			float stepZ = halfZ * 2f / countZ;
			Vector3 forward = metadata.Axis == PrefabRepeatAxis.X ? axisX : axisZ;
			Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
			for (int z = 0; z < countZ; z++)
			{
				float localZ = -halfZ + (z + 0.5f) * stepZ;
				for (int x = 0; x < countX; x++)
				{
					float localX = -halfX + (x + 0.5f) * stepX;
					Vector3 position = center + axisX * localX + axisZ * localZ;
					_placements.Add(new Placement(position, rotation));
					if (_placements.Count > MaximumPlacements) return;
				}
			}
		}

		private void GenerateConcentricRectangle(Vector3 center, Vector3 axisX, Vector3 axisZ,
			float halfX, float halfZ, float spacing, float ringStep,
			GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			var rings = new List<Vector3[]>();
			int requiredPlacements = 0;
			bool addCenterPiece = false;
			for (float inset = 0f; halfX - inset > 0f && halfZ - inset > 0f; inset += ringStep)
			{
				float ringHalfX = halfX - inset;
				float ringHalfZ = halfZ - inset;
				if (ringHalfX * 2f < spacing * 1.5f && ringHalfZ * 2f < spacing * 1.5f)
				{
					addCenterPiece = true;
					requiredPlacements++;
					if (requiredPlacements > MaximumPlacements)
					{
						_shapeLayoutOverLimit = true;
						_placements.Clear();
						return;
					}
					break;
				}
				if (ringHalfX * 2f < spacing * 1.5f || ringHalfZ * 2f < spacing * 1.5f)
					break;
				Vector3[] corners =
				{
					center - axisX * ringHalfX - axisZ * ringHalfZ,
					center + axisX * ringHalfX - axisZ * ringHalfZ,
					center + axisX * ringHalfX + axisZ * ringHalfZ,
					center - axisX * ringHalfX + axisZ * ringHalfZ
				};
				int edgeX = Mathf.Max(1, Mathf.RoundToInt(ringHalfX * 2f / spacing));
				int edgeZ = Mathf.Max(1, Mathf.RoundToInt(ringHalfZ * 2f / spacing));
				requiredPlacements += 2 * edgeX + 2 * edgeZ;
				if (requiredPlacements > MaximumPlacements)
				{
					_shapeLayoutOverLimit = true;
					_placements.Clear();
					return;
				}
				rings.Add(corners);
			}

			foreach (Vector3[] ring in rings)
				GenerateOutlineEdges(ring, spacing, prefab, metadata);
			if (addCenterPiece)
			{
				Vector3 forward = metadata.Axis == PrefabRepeatAxis.X ? axisX : axisZ;
				_placements.Add(new Placement(center, Quaternion.LookRotation(forward, Vector3.up)));
			}
		}

		private float GetShapeRingSpacing(GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			return Mathf.Max(MinimumSpacing, GetShapeRingSpacingBase(prefab, metadata) + _shapeRingSpacingAdjustment);
		}

		private static float GetShapeRingSpacingBase(GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			if (!prefab) return MinimumSpacing;
			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float across = metadata.Axis == PrefabRepeatAxis.X ? bounds.size.z
				: metadata.Axis == PrefabRepeatAxis.Z ? bounds.size.x
				: Mathf.Min(bounds.size.x, bounds.size.z);
			if (float.IsNaN(across) || float.IsInfinity(across) || across < 0.001f)
				return Mathf.Max(MinimumSpacing, metadata.Spacing * 0.5f);
			return Mathf.Max(MinimumSpacing, Mathf.Min(metadata.Spacing, across));
		}

		private string GetShapeFillLabel()
		{
			if (_shapeFillMode == ShapeFillMode.Outline) return "outline only";
			if (_mode == PieceRepeaterMode.Circle) return "concentric infill";
			return _shapeFillMode == ShapeFillMode.Grid ? "grid infill" : "concentric infill";
		}

		private string GetShapeSizeLabel(float pieceSpacing)
		{
			if (_points.Count == 0 || !_hasAim) return "unset";
			Vector3 center = _points[0];
			float dx = Mathf.Abs(_aim.x - center.x);
			float dz = Mathf.Abs(_aim.z - center.z);
			if (_mode == PieceRepeaterMode.Circle)
			{
				float diameter = Vector2.Distance(new Vector2(center.x, center.z), new Vector2(_aim.x, _aim.z)) * 2f;
				return "diameter " + FormatMeters(diameter);
			}

			if (_mode == PieceRepeaterMode.Square)
			{
				float requestedSide = Mathf.Sqrt(dx * dx + dz * dz) * Mathf.Sqrt(2f);
				float side = Mathf.Max(pieceSpacing, Mathf.Round(requestedSide / pieceSpacing) * pieceSpacing);
				return "side " + FormatMeters(side);
			}

			float width = Mathf.Max(pieceSpacing, Mathf.Round(dx * 2f / pieceSpacing) * pieceSpacing);
			float depth = Mathf.Max(pieceSpacing, Mathf.Round(dz * 2f / pieceSpacing) * pieceSpacing);
			return FormatMeters(width) + " × " + FormatMeters(depth);
		}

		private static string FormatMeters(float value)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture) + " m";
		}

		private void RefreshShapeToolCard()
		{
			if (!_shapeToolMode) return;
			if (!InfinityHammerBridge.TryGetActiveToolSelectionPiece(ToolInstaller.ShapeToolName, out Piece piece) || !piece)
			{
				_shapeDescriptionPiece = null;
				_shapeDescriptionValue = null;
				return;
			}

			string description = piece.m_description ?? string.Empty;
			int markerIndex = description.IndexOf(ShapeDescriptionMarker, StringComparison.Ordinal);
			if (markerIndex >= 0) description = description.Substring(0, markerIndex);

			string liveValues;
			if (!string.IsNullOrEmpty(_prefabId) && ZNetScene.instance
			    && ZNetScene.instance.GetPrefab(_prefabId) is GameObject prefab)
			{
				ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
				float pieceSpacing = GetSpacing(prefab);
				liveValues = ShapeDescriptionMarker + _mode + " / " + GetShapeFillLabel()
					+ "\nSize: " + GetShapeSizeLabel(pieceSpacing)
					+ " · Preview: " + (_shapeLayoutOverLimit ? ">" + MaximumPlacements : _placements.Count.ToString(CultureInfo.InvariantCulture))
					+ "\nPiece spacing: " + FormatMeters(pieceSpacing) + " · Gap: " + FormatMeters(_gapMeters)
					+ "\nRotation: " + (_rotationSteps * RotationStepDegrees).ToString("0.#", CultureInfo.InvariantCulture) + "°";
				if (_shapeFillMode == ShapeFillMode.Concentric)
				{
					float ringSpacing = _mode == PieceRepeaterMode.Circle
						? pieceSpacing * 0.8660254f
						: GetShapeRingSpacing(prefab, metadata);
					liveValues += " · Ring spacing: " + FormatMeters(ringSpacing);
				}
			}
			else
			{
				liveValues = ShapeDescriptionMarker + _mode + " / " + GetShapeFillLabel()
					+ "\nSample a piece to show spacing and size.";
			}

			if (_shapeDescriptionPiece == piece && string.Equals(_shapeDescriptionValue, liveValues, StringComparison.Ordinal))
				return;
			if (_shapeDescriptionPiece == piece && Time.frameCount - _shapeDescriptionLastRefreshFrame < 6)
				return;

			piece.m_description = description + liveValues;
			try
			{
				if (Hud.instance && SetupPieceInfoMethod != null)
					SetupPieceInfoMethod.Invoke(Hud.instance, new object[] { piece });
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogDebug("Could not refresh Shape Tool card values: " + ex.Message);
			}
			_shapeDescriptionPiece = piece;
			_shapeDescriptionValue = liveValues;
			_shapeDescriptionLastRefreshFrame = Time.frameCount;
		}

		private void GenerateOutlineEdges(Vector3[] corners, float spacing, GameObject prefab,
			ResolvedPrefabPlacement metadata)
		{
			float inset = GetWidePanelInset(prefab, metadata, spacing, out bool squarePanel);
			float signedArea = 0f;
			for (int i = 0; i < corners.Length; i++)
			{
				Vector3 current = corners[i];
				Vector3 next = corners[(i + 1) % corners.Length];
				signedArea += current.x * next.z - next.x * current.z;
			}
			bool interiorOnLeft = signedArea >= 0f;
			for (int side = 0; side < corners.Length; side++)
			{
				Vector3 start = corners[side];
				Vector3 end = corners[(side + 1) % corners.Length];
				Vector3 edge = end - start;
				float length = edge.magnitude;
				if (length < MinimumSpacing * 0.5f) continue;
				int count = Mathf.Max(1, Mathf.RoundToInt(length / spacing));
				Vector3 dir = edge / length;
				Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
				Vector3 left = new Vector3(-dir.z, 0f, dir.x);
				Vector3 inward = interiorOnLeft ? left : -left;
				for (int i = 0; i < count; i++)
				{
					float t = (i + 0.5f) / count;
					Vector3 position = Vector3.Lerp(start, end, t) + inward * inset;
					if (squarePanel && i == 0 && side > 0 && _placements.Count > 0
					    && (_placements[_placements.Count - 1].Position - position).sqrMagnitude < 0.0025f)
						continue;
					_placements.Add(new Placement(position, rot));
					if (_placements.Count > MaximumPlacements) return;
				}
			}
		}

		private static float GetWidePanelInset(GameObject prefab, ResolvedPrefabPlacement metadata,
			float spacing, out bool squarePanel)
		{
			squarePanel = false;
			if (!prefab || metadata.ProfileType != RepeatProfileType.EdgeChain
			    || !metadata.SupportsX || !metadata.SupportsZ
			    || (metadata.Axis != PrefabRepeatAxis.X && metadata.Axis != PrefabRepeatAxis.Z)) return 0f;
			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float along = metadata.Axis == PrefabRepeatAxis.X ? bounds.size.x : bounds.size.z;
			float across = metadata.Axis == PrefabRepeatAxis.X ? bounds.size.z : bounds.size.x;
			if (along < spacing * 0.75f || across < spacing * 0.75f) return 0f;
			squarePanel = Mathf.Abs(along - across) < spacing * 0.1f;
			return across * 0.5f;
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
			ResolvedPrefabPlacement metadata = PrefabPlacementMetadata.Resolve(_prefabId, prefab);
			if (!AnarchyModeActive || !prefab) return metadata;

			// Anarchy deliberately opts out of catalog join/profile assumptions. Treat
			// every networked prefab as a ground prop and use the configured spacing.
			metadata.Axis = PrefabRepeatAxis.Auto;
			metadata.Spacing = Mathf.Max(0.1f, PluginConfig.PieceRepeaterGroundPropSpacing.Value);
			metadata.CenterOffset = Vector3.zero;
			metadata.PositionOffset = Vector3.zero;
			metadata.JoinStartOffset = Vector3.zero;
			metadata.JoinEndOffset = Vector3.zero;
			metadata.YawOffset = 0f;
			metadata.TangentYawOffset = 0f;
			metadata.BottomOffset = PrefabPlacementMetadata.GetLocalRendererBounds(prefab).min.y;
			metadata.HasBottomOffsetOverride = true;
			metadata.ProfileType = RepeatProfileType.GroundProp;
			metadata.Status = RepeatProfileStatus.Ready;
			metadata.Enabled = true;
			metadata.SupportsX = true;
			metadata.SupportsZ = true;
			metadata.JoinStart = Vector3.zero;
			metadata.JoinEnd = Vector3.zero;
			metadata.MeasurementSource = "anarchy-fallback";
			metadata.Confidence = "Low";
			return metadata;
		}

		private void HandleSnapPointInput(Player player)
		{
			bool previous = ToolInput.WasButtonDown("TabLeft");
			bool next = ToolInput.WasButtonDown("TabRight");
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
			return TryGetInitialSnapPlacement(player, aim, out placement, out _, out _);
		}

		private bool TryGetInitialSnapPlacement(Player player, Vector3 aim, out Placement placement,
			out Vector3 targetAnchorWorld, out Vector3 sourceAnchorLocal)
		{
			placement = default(Placement);
			targetAnchorWorld = Vector3.zero;
			sourceAnchorLocal = Vector3.zero;
			if (!player || !ZNetScene.instance || string.IsNullOrEmpty(_prefabId)) return false;
			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			if (!prefab || !EnsureSnapProbe(prefab)) return false;
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			Placement initial = GetInitialPlacement(prefab, aim, metadata);
			_snapProbe.transform.SetPositionAndRotation(initial.Position, initial.Rotation);
			if (!InfinityHammerBridge.TryFindSnapPair(player, _snapProbe.transform, _snapPointIndex,
				out Transform sourceSnapPoint, out Transform targetSnapPoint))
			{ placement = initial; return false; }

			targetAnchorWorld = targetSnapPoint.position;
			sourceAnchorLocal = _snapProbe.transform.InverseTransformPoint(sourceSnapPoint.position);
			Vector3 snappedRoot = initial.Position + targetSnapPoint.position - sourceSnapPoint.position;
			if (metadata.ProfileType == RepeatProfileType.GroundProp)
				snappedRoot = ApplySurfaceHeightMode(snappedRoot, -metadata.BottomOffset, snappedRoot.y);
			snappedRoot += initial.Rotation * metadata.PositionOffset;
			placement = new Placement(snappedRoot, initial.Rotation);
			return true;
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
			rootPosition += rotation * metadata.PositionOffset;
			return new Placement(rootPosition, rotation);
		}

		private Placement GetAnarchyInitialPlacement(Vector3 aim, ResolvedPrefabPlacement metadata)
		{
			Quaternion rotation = Quaternion.Euler(0f, _sampledYaw, 0f);
			Vector3 rootPosition = aim;
			rootPosition.y = aim.y - metadata.BottomOffset;
			rootPosition = ApplySurfaceHeightMode(rootPosition, -metadata.BottomOffset, rootPosition.y);
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

		private void UpdateLockedStartForDirection(GameObject prefab, ResolvedPrefabPlacement metadata, Vector3 target)
		{
			if (!_hasLockedStartPlacement || !_hasLockedStartAnchor || !prefab) return;
			Vector3 direction = target - _lockedStartAnchorWorld;
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return;
			direction.Normalize();

			Vector3 localAxis = metadata.JoinEnd - metadata.JoinStart;
			localAxis.y = 0f;
			if (localAxis.sqrMagnitude < 0.0001f) return;
			float outgoingAxisSign = PieceRepeaterPathMath.ResolveOutgoingAxisSign(
				_lockedStartAnchorLocal, metadata.JoinStart, metadata.JoinEnd, _lockedStartAnchorIsSnap);
			localAxis.Normalize();
			localAxis *= outgoingAxisSign;
			float desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			float localYaw = Mathf.Atan2(localAxis.x, localAxis.z) * Mathf.Rad2Deg;
			float wheelYaw = (_rotationSteps - _rotationStepsAtStart) * RotationStepDegrees;
			float yaw = desiredYaw - localYaw - metadata.YawOffset + wheelYaw;
			Quaternion rotation = Quaternion.Euler(0f, yaw, 0f)
				* Quaternion.Euler(0f, metadata.YawOffset, 0f);
			Vector3 root = _lockedStartAnchorWorld - rotation * _lockedStartAnchorLocal
				+ rotation * metadata.PositionOffset;
			_lockedStartPlacement = new Placement(root, rotation);
			_points[0] = root + rotation * metadata.CenterOffset;
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
						Vector3 previewWaypoint = GetPathWaypoint(_aim, pending);
						Vector3 previewLeg = previewWaypoint - pending[pending.Count - 1];
						previewLeg.y = 0f;
						// A repeated endpoint creates a degenerate Catmull-Rom segment
						// that can loop beyond the last committed point. Keep the preview
						// identical to the committed route until the cursor advances.
						if (previewLeg.sqrMagnitude >= 0.0001f) pending.Add(previewWaypoint);
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
			RefreshShapeToolCard();
		}

		private void GenerateLayoutWithAim(GameObject prefab, Vector3 start, Vector3 aim)
		{
			Vector3 savedAim = _aim;
			_aim = aim;
			GenerateLayout(prefab);
			_aim = savedAim;
		}

		private void LogWaypointSolve(IList<Placement> previous, Vector3 waypoint)
		{
			int comparable = Mathf.Min(previous != null ? previous.Count : 0, _placements.Count);
			int changed = 0;
			float maxPositionDelta = 0f;
			float maxRotationDelta = 0f;
			for (int i = 0; i < comparable; i++)
			{
				float positionDelta = Vector3.Distance(previous[i].Position, _placements[i].Position);
				float rotationDelta = Quaternion.Angle(previous[i].Rotation, _placements[i].Rotation);
				maxPositionDelta = Mathf.Max(maxPositionDelta, positionDelta);
				maxRotationDelta = Mathf.Max(maxRotationDelta, rotationDelta);
				if (positionDelta > 0.02f || rotationDelta > 1f) changed++;
			}

			string message = "prefab=" + _prefabId + " committed=" + _points.Count
				+ " point=" + waypoint.ToString("F2")
				+ " previousPreview=" + (previous != null ? previous.Count : 0)
				+ " solvedPreview=" + _placements.Count
				+ " changedExisting=" + changed + "/" + comparable
				+ " maxPositionDelta=" + maxPositionDelta.ToString("0.000") + "m"
				+ " maxRotationDelta=" + maxRotationDelta.ToString("0.0") + "deg";
			PieceRepeaterDiagnostics.Log("WaypointSolve", message);
		}

		private static HashSet<int> GetWearNTearInstanceIds()
		{
			var ids = new HashSet<int>();
			List<WearNTear> instances = WearNTear.GetAllInstances();
			if (instances == null) return ids;
			for (int i = 0; i < instances.Count; i++)
				if (instances[i]) ids.Add(instances[i].GetInstanceID());
			return ids;
		}

		private static List<WearNTear> FindNewMatchingInstances(HashSet<int> before, string prefabId, Placement requested)
		{
			var matches = new List<WearNTear>();
			List<WearNTear> instances = WearNTear.GetAllInstances();
			if (instances == null) return matches;
			for (int i = 0; i < instances.Count; i++)
			{
				WearNTear instance = instances[i];
				if (!instance || before.Contains(instance.GetInstanceID())) continue;
				string instanceName = instance.gameObject.name;
				if (instanceName.EndsWith("(Clone)", StringComparison.Ordinal))
					instanceName = instanceName.Substring(0, instanceName.Length - 7);
				if (!string.Equals(instanceName, prefabId, StringComparison.OrdinalIgnoreCase)) continue;
				if (Vector3.Distance(instance.transform.position, requested.Position) > 0.1f
				    || Quaternion.Angle(instance.transform.rotation, requested.Rotation) > 2f) continue;
				matches.Add(instance);
			}
			return matches;
		}

		private void VerifyRecentPlacements()
		{
			for (int i = _placementVerifications.Count - 1; i >= 0; i--)
			{
				PlacementVerification verification = _placementVerifications[i];
				if (Time.unscaledTime < verification.VerifyAfter) continue;
				GameObject instance = verification.Instance;
				if (!instance)
				{
					PieceRepeaterDiagnostics.Log("DelayedCheck", "prefab=" + verification.PrefabId + " aliveAfter2s=false");
					_placementVerifications.RemoveAt(i);
					continue;
				}

				WearNTear wear = instance.GetComponent<WearNTear>();
				string support = "n/a";
				if (wear)
				{
					try
					{
						object value = HarmonyLib.AccessTools.Method(typeof(WearNTear), "GetSupport")?.Invoke(wear, null);
						support = value is float measured ? measured.ToString("0.###") : "unavailable";
					}
					catch (Exception ex) { support = "error:" + ex.GetType().Name; }
				}
				PieceRepeaterDiagnostics.Log("DelayedCheck", "prefab=" + verification.PrefabId
					+ " aliveAfter2s=true instance=" + instance.name
					+ " requested=" + verification.RequestedPosition.ToString("F3")
					+ " actual=" + instance.transform.position.ToString("F3")
					+ " positionDelta=" + Vector3.Distance(verification.RequestedPosition, instance.transform.position).ToString("0.000")
					+ "m rotationDelta=" + Quaternion.Angle(verification.RequestedRotation, instance.transform.rotation).ToString("0.0")
					+ "deg support=" + support
					+ " health=" + (wear ? wear.GetHealthPercentage().ToString("0.###") : "n/a"));
				_placementVerifications.RemoveAt(i);
			}
		}

		private static float GetJoinError(Placement previous, Placement current, GameObject prefab)
		{
			ResolvedPrefabPlacement metadata = PrefabPlacementMetadata.Resolve(prefab.name, prefab);
			Vector3 delta = current.Position - previous.Position;
			delta.y = 0f;
			if (delta.sqrMagnitude < 0.0001f) return 0f;
			Vector3 previousAxis = previous.Rotation * (metadata.JoinEnd - metadata.JoinStart);
			Vector3 currentAxis = current.Rotation * (metadata.JoinEnd - metadata.JoinStart);
			previousAxis.y = currentAxis.y = 0f;
			Vector3 direction = delta.normalized;
			Vector3 exit = previous.Position + previous.Rotation
				* (Vector3.Dot(previousAxis, direction) >= 0f ? metadata.JoinEnd : metadata.JoinStart);
			Vector3 entry = current.Position + current.Rotation
				* (Vector3.Dot(currentAxis, direction) >= 0f ? metadata.JoinStart : metadata.JoinEnd);
			Vector3 error = entry - exit;
			error.y = 0f;
			return error.magnitude;
		}

		private void BuildEdgeChainPath(IList<Vector3> path, float spacing, GameObject prefab,
			ResolvedPrefabPlacement metadata, float groundOffset, float fixedHeight,
			IList<int> rawCounts, IList<int> duplicateCounts)
		{
			if (path == null || path.Count < 2) return;
			spacing = Mathf.Max(MinimumSpacing, spacing);
			for (int i = 0; i + 1 < path.Count; i++)
			{
				rawCounts?.Add(0);
				duplicateCounts?.Add(0);
			}

			Vector3 chainStart = metadata.JoinStart;
			Vector3 chainEnd = metadata.JoinEnd;
			Vector3 hinge = _hasLockedStartPlacement
				? _lockedStartPlacement.Position + _lockedStartPlacement.Rotation * metadata.CenterOffset
				: path[0];
			_lastPathCenterPivotCount = 0;
			_lastPathInsidePivotCount = 0;
			_lastPathInsideEdgeErrorMax = 0f;
			int segment = 0;
			float segmentT = 0f;
			int guard = 0;
			while (_placements.Count < MaximumPlacements && guard++ < MaximumPlacements)
			{
				if (!TryFindNextChordPoint(path, hinge, spacing, segment, segmentT,
					out Vector3 nextHinge, out int nextSegment, out float nextT)) break;
				Vector3 chord = nextHinge - hinge;
				chord.y = 0f;
				if (chord.sqrMagnitude < 0.0001f) break;
				chord.Normalize();

				float wheelYaw = (_hasLockedStartPlacement ? _rotationSteps - _rotationStepsAtStart : _rotationSteps)
					* RotationStepDegrees;
				Quaternion rotation = GetEdgeChainRotation(chord, chainEnd - chainStart, wheelYaw);
				Placement previous = _placements.Count > 0
					? _placements[_placements.Count - 1]
					: new Placement(hinge - rotation * chainStart, rotation);
				Placement candidate = new Placement(hinge - rotation * chainStart, rotation);
				candidate.Position = ApplySurfaceHeightMode(candidate.Position, groundOffset, fixedHeight);

			GetInsidePivotSides(previous.Rotation * (chainEnd - chainStart),
				candidate.Rotation * (chainEnd - chainStart), out Vector3 previousInsideSide,
				out Vector3 nextInsideSide, out float turn);
			Vector3 previousInside = GetEdgeJoinPoint(previous, prefab, metadata, chainEnd, previousInsideSide);
			Vector3 nextInside = candidate.Position + GetEdgeJoinPoint(
				new Placement(Vector3.zero, candidate.Rotation), prefab, metadata, chainStart, nextInsideSide);
			Vector3 insideError = nextInside - previousInside;
			insideError.y = 0f;
			_lastPathInsideEdgeErrorMax = Mathf.Max(_lastPathInsideEdgeErrorMax, insideError.magnitude);
			bool pivotInside = Mathf.Abs(turn) >= 90f || insideError.magnitude > 0.02f;
			if (pivotInside)
			{
				_lastPathInsidePivotCount++;
				candidate.Position.x += previousInside.x - nextInside.x;
				candidate.Position.z += previousInside.z - nextInside.z;
				candidate.Position = ApplySurfaceHeightMode(candidate.Position, groundOffset, fixedHeight);
			}
			else _lastPathCenterPivotCount++;
			_placements.Add(candidate);

			if (pivotInside && nextInsideSide.sqrMagnitude > 0.0001f)
				hinge = GetEdgeJoinPoint(candidate, prefab, metadata, chainEnd, nextInsideSide);
			else
				hinge = candidate.Position + candidate.Rotation * chainEnd;
			segment = nextSegment;
			segmentT = nextT;
			if (nextSegment >= 0 && nextSegment < rawCounts.Count)
				rawCounts[nextSegment] = rawCounts[nextSegment] + 1;
			}
		}

		private static Quaternion GetEdgeChainRotation(Vector3 worldDirection, Vector3 localAxis, float extraYaw)
		{
			worldDirection.y = 0f;
			localAxis.y = 0f;
			if (worldDirection.sqrMagnitude < 0.0001f || localAxis.sqrMagnitude < 0.0001f)
				return Quaternion.identity;
			worldDirection.Normalize();
			localAxis.Normalize();
			float worldYaw = Mathf.Atan2(worldDirection.x, worldDirection.z) * Mathf.Rad2Deg;
			float localYaw = Mathf.Atan2(localAxis.x, localAxis.z) * Mathf.Rad2Deg;
			return Quaternion.Euler(0f, worldYaw - localYaw + extraYaw, 0f);
		}

		private static void GetInsidePivotSides(Vector3 previousDirection, Vector3 nextDirection,
			out Vector3 previousInsideSide, out Vector3 nextInsideSide, out float turn)
		{
			previousDirection.y = nextDirection.y = 0f;
			if (previousDirection.sqrMagnitude > 0.0001f) previousDirection.Normalize();
			if (nextDirection.sqrMagnitude > 0.0001f) nextDirection.Normalize();
			turn = Vector3.SignedAngle(previousDirection, nextDirection, Vector3.up);
			if (Mathf.Abs(turn) < 0.01f)
			{
				previousInsideSide = nextInsideSide = Vector3.zero;
				return;
			}
			// In Unity (+Z forward), positive signed yaw turns right. Cross(up, forward)
			// is the right side; invert it for a left turn to get the curve interior.
			float turnSign = Mathf.Sign(turn);
			previousInsideSide = Vector3.Cross(Vector3.up, previousDirection) * turnSign;
			nextInsideSide = Vector3.Cross(Vector3.up, nextDirection) * turnSign;
		}

		private static bool TryFindNextChordPoint(IList<Vector3> path, Vector3 from, float chordLength,
			int startSegment, float startT, out Vector3 result, out int resultSegment, out float resultT)
		{
			result = Vector3.zero;
			resultSegment = -1;
			resultT = 0f;
			float radiusSquared = chordLength * chordLength;
			for (int i = Mathf.Clamp(startSegment, 0, path.Count - 2); i < path.Count - 1; i++)
			{
				float minT = i == startSegment ? Mathf.Clamp01(startT) : 0f;
				Vector3 a = Vector3.Lerp(path[i], path[i + 1], minT);
				Vector3 d = path[i + 1] - a;
				d.y = 0f;
				Vector3 offset = a - from;
				offset.y = 0f;
				float qa = Vector3.Dot(d, d);
				if (qa < 0.000001f) continue;
				float qb = 2f * Vector3.Dot(offset, d);
				float qc = Vector3.Dot(offset, offset) - radiusSquared;
				float discriminant = qb * qb - 4f * qa * qc;
				if (discriminant < 0f) continue;
				float root = Mathf.Sqrt(discriminant);
				float first = (-qb - root) / (2f * qa);
				float second = (-qb + root) / (2f * qa);
				float fraction = first > 0.0001f && first <= 1.0001f ? first
					: second > 0.0001f && second <= 1.0001f ? second : -1f;
				if (fraction < 0f) continue;
				fraction = Mathf.Clamp01(fraction);
				result = a + d * fraction;
				result.y = Mathf.Lerp(a.y, path[i + 1].y, fraction);
				resultSegment = i;
				resultT = minT + (1f - minT) * fraction;
				return true;
			}
			return false;
		}

		private void GeneratePathPreview(IList<Vector3> points, float spacing, GameObject prefab)
		{
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			// Re-solve the whole waypoint sequence as a Catmull–Rom spline. The
			// resulting placements remain rigid; only their centers and headings
			// are fitted to the interpolated curve. Rebuilding here is intentional:
			// each newly clicked waypoint updates the complete preview before commit.
			IList<Vector3> layoutPoints = points;
			if (PluginConfig.PieceRepeaterRoundedCorners.Value)
			{
				layoutPoints = PluginConfig.PieceRepeaterCurveAlgorithm.Value == LineToolCurveAlgorithm.CentripetalCatmullRom
					? PieceRepeaterPathMath.SampleCentripetalCatmullRom(points)
					: PieceRepeaterPathMath.SampleCatmullRom(points, Mathf.Clamp(PluginConfig.PieceRepeaterCornerRadius.Value, 0.25f, 6f));
			}
			float groundOffset = -metadata.BottomOffset;
			float firstPointCenterY = _points.Count > 0 ? _points[0].y : 0f;
			float fixedHeight = PieceRepeaterPathMath.ResolveFixedRootHeight(firstPointCenterY,
				metadata.CenterOffset.y, _hasLockedStartPlacement, _lockedStartPlacement.Position.y);
			var rawCounts = new List<int>();
			var duplicateCounts = new List<int>();
			_lastPathCenterDeviation = 0f;
			if (metadata.ProfileType == RepeatProfileType.EdgeChain && !_bothSides)
			{
				foreach (Vector3 point in layoutPoints)
					_outline.Add(ApplySurfaceHeightMode(point, groundOffset, fixedHeight));
				if (_hasLockedStartPlacement) _placements.Add(_lockedStartPlacement);
				BuildSplineFittedEdgeChain(layoutPoints, spacing, prefab, metadata, groundOffset, fixedHeight,
					rawCounts, duplicateCounts);
				ExpandPlacementsDownToTerrain(prefab);
				LogPathDiagnostics(points, spacing, metadata, rawCounts, duplicateCounts, prefab);
				return;
			}
			bool reverseEdgeChain = IsReverseEdgeChain(points, metadata);
			Vector3 chainStart = reverseEdgeChain ? metadata.JoinEnd : metadata.JoinStart;
			Vector3 chainEnd = reverseEdgeChain ? metadata.JoinStart : metadata.JoinEnd;

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
						GetCornerPivotSides(previous.Rotation, connected.Rotation, chainStart, chainEnd,
							out Vector3 previousDirection, out Vector3 nextDirection,
							out Vector3 previousCornerSide, out Vector3 nextCornerSide, out float turn);
						Vector3 previousJoin = GetEdgeJoinPoint(previous, prefab, metadata, chainEnd, previousCornerSide);
						Vector3 nextJoinOffset = GetEdgeJoinPoint(new Placement(Vector3.zero, connected.Rotation), prefab, metadata, chainStart, nextCornerSide);
						Vector3 previousCenter = previous.Position + previous.Rotation * chainEnd;
						Vector3 nextCenterOffset = connected.Rotation * chainStart;
						Vector3 centeredRoot = previousCenter + nextDirection * _gapMeters - nextCenterOffset;
						Vector3 cornerRoot = previousJoin + nextDirection * _gapMeters - nextJoinOffset;
						Vector3 expectedAnchorDelta = nextDirection * _gapMeters;
						Placement centeredCandidate = new Placement(centeredRoot, connected.Rotation);
						float outsideBreak = GetCenterPivotAnchorError(previous, centeredCandidate, prefab, metadata,
							chainStart, chainEnd, previousCornerSide, nextCornerSide, expectedAnchorDelta);
						float insideBreak = GetCenterPivotAnchorError(previous, centeredCandidate, prefab, metadata,
							chainStart, chainEnd, -previousCornerSide, -nextCornerSide, expectedAnchorDelta);
						bool useOutsideCorner = ShouldPivotAtOutsideCorner(turn, outsideBreak, insideBreak);
						Vector3 selectedRoot = useOutsideCorner ? cornerRoot : centeredRoot;
						connected.Position.x = selectedRoot.x;
						connected.Position.z = selectedRoot.z;
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
			ExpandPlacementsDownToTerrain(prefab);
			LogPathDiagnostics(points, spacing, metadata, rawCounts, duplicateCounts, prefab);
		}

		// When level height is active, optionally build a vertical column below every
		// solved path placement. Lower rows are ordered first so pieces have support
		// before the elevated route is requested from Valheim.
		private void ExpandPlacementsDownToTerrain(GameObject prefab)
		{
			if (!PluginConfig.PieceRepeaterFillDownward.Value || !IsSameHeightHeld()
			    || !prefab || _placements.Count == 0) return;

			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float pieceHeight = bounds.size.y;
			if (pieceHeight < MinimumSpacing || float.IsNaN(pieceHeight) || float.IsInfinity(pieceHeight)) return;

			var bases = new List<Placement>(_placements);
			var fillDepths = new List<float>(bases.Count);
			var fillCounts = new List<int>(bases.Count);
			int maximumLayers = 0;
			int totalCopies = 0;
			for (int i = 0; i < bases.Count; i++)
			{
				Placement placement = bases[i];
				if (!TryGetTerrainHeightForDownwardFill(placement.Position, out float terrainHeight))
				{
					fillDepths.Add(0f);
					fillCounts.Add(0);
					continue;
				}

				float depth = placement.Position.y + bounds.min.y - terrainHeight;
				if (depth <= 0.02f || depth > MaximumDownwardFillDepth)
				{
					fillDepths.Add(0f);
					fillCounts.Add(0);
					continue;
				}

				int copies = Mathf.CeilToInt(depth / pieceHeight);
				fillDepths.Add(depth);
				fillCounts.Add(copies);
				totalCopies += copies;
				maximumLayers = Mathf.Max(maximumLayers, copies);
			}

			// Avoid a partial column or a preview that cannot be placed atomically.
			if (bases.Count + totalCopies > MaximumPlacements)
			{
				if (PluginConfig.LineToolDiagnostics.Value)
					PieceRepeaterDiagnostics.Log("DownwardFillSkipped", "prefab=" + _prefabId
						+ " basePlacements=" + bases.Count + " downwardCopies=" + totalCopies
						+ " limit=" + MaximumPlacements);
				return;
			}

			var expanded = new List<Placement>(bases.Count + totalCopies);
			for (int layer = maximumLayers; layer >= 1; layer--)
			{
				for (int i = 0; i < bases.Count; i++)
				{
					if (fillCounts[i] < layer) continue;
					Placement lower = bases[i];
					float depth = fillDepths[i];
					float down = layer == fillCounts[i] ? depth : pieceHeight * layer;
					lower.Position.y -= down;
					expanded.Add(lower);
				}
			}
			expanded.AddRange(bases);
			_placements.Clear();
			_placements.AddRange(expanded);
		}

		private static bool TryGetTerrainHeightForDownwardFill(Vector3 position, out float terrainHeight)
		{
			terrainHeight = 0f;
			if (!ZoneSystem.instance) return false;
			// This bool API distinguishes a valid ground sample from GetSolidHeight's
			// zero fallback, which otherwise creates spurious columns toward world Y=0.
			return ZoneSystem.instance.GetGroundHeight(position, out terrainHeight)
			       && !float.IsNaN(terrainHeight)
			       && !float.IsInfinity(terrainHeight)
			       && position.y - terrainHeight <= MaximumDownwardFillDepth + 0.02f;
		}

		// Fits rigid edge-chain pieces to the interpolated route. Piece centers
		// advance by the largest local step whose longitudinal projections overlap;
		// this naturally tightens spacing through S-curves without deforming pieces.
		private void BuildSplineFittedEdgeChain(IList<Vector3> path, float spacing, GameObject prefab,
			ResolvedPrefabPlacement metadata, float groundOffset, float fixedHeight,
			IList<int> rawCounts, IList<int> duplicateCounts)
		{
			if (path == null || path.Count < 2) return;
			var distances = new List<float>(path.Count) { 0f };
			for (int i = 1; i < path.Count; i++)
			{
				Vector3 delta = path[i] - path[i - 1]; delta.y = 0f;
				distances.Add(distances[i - 1] + delta.magnitude);
			}
			float total = distances[distances.Count - 1];
			if (total < MinimumSpacing) return;

			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			Vector3 joinDelta = metadata.JoinEnd - metadata.JoinStart;
			joinDelta.y = 0f;
			float length = Mathf.Max(MinimumSpacing, joinDelta.magnitude);
			float width = Mathf.Max(0.01f, metadata.Axis == PrefabRepeatAxis.X
				? bounds.size.z : bounds.size.x);
			Vector3 localAxis = metadata.JoinEnd - metadata.JoinStart;
			localAxis.y = 0f;
			if (localAxis.sqrMagnitude < 0.0001f) return;
			// Use the same outgoing end chosen for the anchored first piece. The
			// sign is applied exactly once in both the starter and fitted run.
			if (_hasLockedStartPlacement)
				localAxis *= PieceRepeaterPathMath.ResolveOutgoingAxisSign(
					_lockedStartAnchorLocal, metadata.JoinStart, metadata.JoinEnd, _lockedStartAnchorIsSnap);

			Placement previous = _hasLockedStartPlacement
				? _lockedStartPlacement
				: new Placement(path[0] - Quaternion.identity * metadata.JoinEnd, Quaternion.identity);
			Vector3 previousCenter = previous.Position + previous.Rotation * metadata.CenterOffset;
			float wheelYaw = (_hasLockedStartPlacement ? _rotationSteps - _rotationStepsAtStart : _rotationSteps) * RotationStepDegrees;
			// The first piece may already include the user's wheel offset when its
			// secant heading was resolved. Remove that offset for fit calculations;
			// manual rotation changes orientation in place, not the solved centers.
			Quaternion fitStartRotation = _hasLockedStartPlacement
				? Quaternion.Euler(0f, -wheelYaw, 0f) * previous.Rotation
				: previous.Rotation;
			Vector3 previousForward = fitStartRotation * localAxis.normalized;
			previousForward.y = 0f; previousForward.Normalize();
			Vector3 routeForward = path[1] - path[0];
			routeForward.y = 0f;
			if (routeForward.sqrMagnitude > 0.0001f)
			{
				routeForward.Normalize();
				// Guard against numerical disagreement at a nearly perpendicular
				// starter heading while keeping the selected outgoing end consistent.
				if (Vector3.Dot(previousForward, routeForward) < 0f) previousForward = -previousForward;
			}
			float cursor = 0f;
			int guard = 0;
			while (_placements.Count < MaximumPlacements && guard++ < MaximumPlacements)
			{
				if (!PieceRepeaterPathMath.TryFindNextCenter(path, distances, cursor, total,
					previousCenter, previousForward, localAxis, length, width, _gapMeters,
					MinimumSpacing, 0f, out float accepted, out Vector3 nextCenter, out Quaternion solvedRotation)) break;
				// Wheel rotation is an in-place orientation adjustment. Keep it out of
				// the spacing solver so rotating the pieces cannot change their centers
				// or make the batch gain/lose pieces.
				Quaternion placementRotation = Quaternion.Euler(0f, wheelYaw, 0f) * solvedRotation;
				Vector3 correction = placementRotation * metadata.PositionOffset;
				Vector3 root = nextCenter - placementRotation * metadata.CenterOffset + correction;
				if (_placements.Count > 0)
				{
					Placement last = _placements[_placements.Count - 1];
					Vector3 chainStart = Vector3.Dot(localAxis, joinDelta) >= 0f
						? metadata.JoinStart : metadata.JoinEnd;
					Vector3 chainEnd = Vector3.Dot(localAxis, joinDelta) >= 0f
						? metadata.JoinEnd : metadata.JoinStart;
					GetCornerPivotSides(last.Rotation, placementRotation, chainStart, chainEnd,
						out _, out _, out Vector3 lastOutside, out Vector3 nextOutside, out float turn);
					if (Mathf.Abs(turn) >= 1f)
					{
						Vector3 lastJoin = GetEdgeJoinPoint(last, prefab, metadata, chainEnd, lastOutside);
						Vector3 nextJoin = GetEdgeJoinPoint(new Placement(root, placementRotation),
							prefab, metadata, chainStart, nextOutside);
						// A join may move the rendered piece, but never the curve cursor or
						// the next solver center. Cap the move so a bad join cannot grow a
						// placed run beyond its waypoints.
						Vector3 joinCorrection = PieceRepeaterPathMath.BoundedJoinCorrection(
							lastJoin, nextJoin, Mathf.Min(0.6f, Mathf.Max(0.1f, width * 2f)));
						root += joinCorrection;
					}
				}
				root = ApplySurfaceHeightMode(root, groundOffset, fixedHeight);
				root.y += correction.y;
				_placements.Add(new Placement(root, placementRotation));
				// Keep the solver on the sampled route. Per-piece root/height
				// corrections affect rendering, never the next center search.
				previousCenter = nextCenter;
				previousForward = solvedRotation * localAxis.normalized; previousForward.y = 0f; previousForward.Normalize();
				cursor = accepted;
			}
		}

		private static Vector3 GetEdgeJoinPoint(Placement placement, GameObject prefab,
			ResolvedPrefabPlacement metadata, Vector3 centerJoin, Vector3 preferredWorldSide)
		{
			if (preferredWorldSide.sqrMagnitude < 0.0001f)
				return placement.Position + placement.Rotation * centerJoin;

			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			Vector3 transverse = metadata.Axis == PrefabRepeatAxis.X ? Vector3.forward : Vector3.right;
			float transverseMin = metadata.Axis == PrefabRepeatAxis.X ? bounds.min.z : bounds.min.x;
			float transverseMax = metadata.Axis == PrefabRepeatAxis.X ? bounds.max.z : bounds.max.x;
			if (transverseMax - transverseMin <= 0.001f)
				return placement.Position + placement.Rotation * centerJoin;

			// Repeat joints come from the intact rendered footprint. Build snap
			// points are used only for the first vanilla-style anchor; their height
			// and naming conventions must not steer path corners.
			Vector3 desiredLocalSide = Quaternion.Inverse(placement.Rotation) * preferredWorldSide;
			float desiredTransverse = Vector3.Dot(desiredLocalSide, transverse) >= 0f
				? transverseMax : transverseMin;
			Vector3 localCorner = centerJoin;
			if (metadata.Axis == PrefabRepeatAxis.X) localCorner.z = desiredTransverse;
			else localCorner.x = desiredTransverse;
			return placement.Position + placement.Rotation * localCorner;
		}

		private static void GetCornerPivotSides(Quaternion previousRotation, Quaternion nextRotation,
			Vector3 chainStart, Vector3 chainEnd, out Vector3 previousDirection, out Vector3 nextDirection,
			out Vector3 previousCornerSide, out Vector3 nextCornerSide, out float turn)
		{
			previousDirection = previousRotation * (chainEnd - chainStart);
			nextDirection = nextRotation * (chainEnd - chainStart);
			previousDirection.y = nextDirection.y = 0f;
			if (previousDirection.sqrMagnitude > 0.0001f) previousDirection.Normalize();
			if (nextDirection.sqrMagnitude > 0.0001f) nextDirection.Normalize();
			turn = Vector3.SignedAngle(previousDirection, nextDirection, Vector3.up);
			if (Mathf.Abs(turn) < 1f)
			{
				previousCornerSide = nextCornerSide = Vector3.zero;
				return;
			}

			Vector3 previousSide = new Vector3(-previousDirection.z, 0f, previousDirection.x);
			Vector3 nextSide = new Vector3(-nextDirection.z, 0f, nextDirection.x);
			// The outside is the same lateral side of both consecutive pieces.
			float outsideSign = turn < 0f ? -1f : 1f;
			previousCornerSide = previousSide * outsideSign;
			nextCornerSide = nextSide * outsideSign;
		}

		private static bool ShouldPivotAtOutsideCorner(float turn, float outsideBreak, float insideBreak)
		{
			const float seamBreakTolerance = 0.02f;
			return Mathf.Abs(turn) >= 90f || Mathf.Max(outsideBreak, insideBreak) > seamBreakTolerance;
		}

		private static float GetCenterPivotAnchorError(Placement previous, Placement next, GameObject prefab,
			ResolvedPrefabPlacement metadata, Vector3 chainStart, Vector3 chainEnd,
			Vector3 previousSide, Vector3 nextSide, Vector3 expectedDelta)
		{
			Vector3 previousAnchor = GetEdgeJoinPoint(previous, prefab, metadata, chainEnd, previousSide);
			Vector3 nextAnchor = next.Position + GetEdgeJoinPoint(
				new Placement(Vector3.zero, next.Rotation), prefab, metadata, chainStart, nextSide);
			Vector3 error = nextAnchor - previousAnchor - expectedDelta;
			error.y = 0f;
			return error.magnitude;
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
			details.Append("[Line Tool] PathTrace prefab=").Append(_prefabId)
				.Append(" profileType=").Append(metadata.ProfileType)
				.Append(" axis=").Append(metadata.Axis)
				.Append(" metadataSpacing=").Append(metadata.Spacing.ToString("0.###"))
				.Append(" joinStart=").Append(metadata.JoinStart.ToString("F3"))
				.Append(" joinEnd=").Append(metadata.JoinEnd.ToString("F3"))
				.Append(" joinLength=").Append((metadata.JoinEnd - metadata.JoinStart).magnitude.ToString("0.###"))
				.Append(" gapMeters=").Append(_gapMeters.ToString("0.###"))
				.Append(" effectiveSpacing=").Append(spacing.ToString("0.###"))
				.Append(" centerOffset=").Append(metadata.CenterOffset.ToString("F2"))
				.Append(" positionOffset=").Append(metadata.PositionOffset.ToString("F3"))
				.Append(" yawOffset=").Append(metadata.YawOffset.ToString("0.###"))
				.Append(" tangentYawOffset=").Append(metadata.TangentYawOffset.ToString("0.###"))
				.Append(" renderLocal=").Append(renderedBounds.center.ToString("F2"))
				.Append(" renderSize=").Append(renderedBounds.size.ToString("F2"))
				.Append(" renderAxisEdges=").Append(renderedAxisMin.ToString("0.###")).Append("..").Append(renderedAxisMax.ToString("0.###"))
				.Append(" rotationSteps=").Append(_rotationSteps)
					.Append(" rotationStepsAtStart=").Append(_rotationStepsAtStart)
					.Append(" pathRotationDelta=").Append(_hasLockedStartPlacement ? _rotationSteps - _rotationStepsAtStart : _rotationSteps)
				.Append(" outgoingJoin=JoinEnd")
				.Append(" curve=").Append(PluginConfig.PieceRepeaterRoundedCorners.Value
					? (PluginConfig.PieceRepeaterCurveAlgorithm.Value == LineToolCurveAlgorithm.CentripetalCatmullRom ? "CentripetalCatmullRom-B" : "CurrentHermite-A")
					: "Linear")
				.Append(" smoothingDistance=").Append(PluginConfig.PieceRepeaterCurveAlgorithm.Value == LineToolCurveAlgorithm.CurrentHermite
					? PluginConfig.PieceRepeaterCornerRadius.Value.ToString("0.##") : "ignored")
				.Append(" edgeSolver=rigid-projection-overlap-first-boundary")
				.Append(" snapLocalDiagnosticOnly=");
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
			bool fittedSpline = _mode == PieceRepeaterMode.Path
				&& metadata.ProfileType == RepeatProfileType.EdgeChain && !_bothSides;
			for (int i = 0; i + 1 < points.Count; i++)
			{
				if (i > 0) details.Append(';');
				float length = Vector2.Distance(new Vector2(points[i].x, points[i].z), new Vector2(points[i + 1].x, points[i + 1].z));
				details.Append(i).Append(':').Append(points[i].ToString("F1")).Append("->").Append(points[i + 1].ToString("F1"))
					.Append("/").Append(length.ToString("0.##")).Append("m");
				if (fittedSpline)
					details.Append("/spline-fit");
				else
				{
					int candidates = i < rawCounts.Count ? rawCounts[i] : 0;
					int skipped = i < duplicateCounts.Count ? duplicateCounts[i] : 0;
					details.Append('/').Append(candidates).Append(" candidates/").Append(candidates - skipped)
						.Append(" kept/").Append(skipped).Append(" overlap-skipped");
				}
			}
			details.Append(" final=").Append(_placements.Count)
				.Append(" centerIntervalsXZ=");
			int logged = Mathf.Min(_placements.Count, 24);
			for (int i = 0; i < logged; i++)
			{
				if (i > 0) details.Append(',');
				Placement placement = _placements[i];
				Vector3 center = placement.Position + placement.Rotation * metadata.CenterOffset;
				details.Append(center.ToString("F1"));
				if (i > 0 && metadata.ProfileType == RepeatProfileType.EdgeChain)
				{
					Placement prior = _placements[i - 1];
					Vector3 priorCenter = prior.Position + prior.Rotation * metadata.CenterOffset;
					Vector3 delta = center - priorCenter; delta.y = 0f;
					float interval = delta.magnitude;
					if (interval > 0.0001f)
					{
						Vector3 q = delta / interval;
						Vector3 axis = (metadata.JoinEnd - metadata.JoinStart).normalized;
						Vector3 f0 = prior.Rotation * axis; f0.y = 0f; f0.Normalize();
						Vector3 f1 = placement.Rotation * axis; f1.y = 0f; f1.Normalize();
						Vector3 side0 = Vector3.Cross(Vector3.up, f0);
						Vector3 side1 = Vector3.Cross(Vector3.up, f1);
						Bounds b = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
						float len = (metadata.JoinEnd - metadata.JoinStart).magnitude;
						float wid = metadata.Axis == PrefabRepeatAxis.X ? b.size.z : b.size.x;
						float reach = len * 0.5f * (Vector3.Dot(f0, q) + Vector3.Dot(f1, q))
							- wid * 0.5f * Mathf.Abs(Vector3.Dot(side0, q) - Vector3.Dot(side1, q)) + _gapMeters;
						Vector3 centerDelta = center - priorCenter;
						centerDelta.y = 0f;
						Vector3 joinAxis = metadata.JoinEnd - metadata.JoinStart;
						joinAxis.y = 0f;
						Vector3 previousAxis = prior.Rotation * joinAxis;
						Vector3 currentAxis = placement.Rotation * joinAxis;
						bool previousForwardPositive = Vector3.Dot(previousAxis, centerDelta) >= 0f;
						bool currentForwardPositive = Vector3.Dot(currentAxis, centerDelta) >= 0f;
						Vector3 previousExit = prior.Position + prior.Rotation
							* (previousForwardPositive ? metadata.JoinEnd : metadata.JoinStart);
						Vector3 currentEntry = placement.Position + placement.Rotation
							* (currentForwardPositive ? metadata.JoinStart : metadata.JoinEnd);
						Vector3 joinError = currentEntry - previousExit;
						joinError.y = 0f;
						Vector3 along = centerDelta.normalized;
						float longitudinalError = Vector3.Dot(joinError, along);
						Vector3 lateralError = joinError - along * longitudinalError;
						lateralError.y = 0f;
						details.Append("(d=").Append(interval.ToString("0.00"))
							.Append("m,slack=").Append((reach - interval).ToString("0.00"))
							.Append("m,joinGap=").Append(longitudinalError.ToString("0.00"))
							.Append("m,joinSide=").Append(lateralError.magnitude.ToString("0.00")).Append("m)");
					}
				}
			}
			if (_placements.Count > logged) details.Append(",...");
			PieceRepeaterDiagnostics.Log("PathTrace", details.ToString());
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
			// terrain sample. A 40 m tolerance accepted that sentinel on ordinary hills
			// and sent some pieces to y=0.5 while adjacent pieces stayed at terrain height.
			// Waypoints are aimed at visible surfaces, so a sample farther than 8 m from
			// the requested height is treated as unavailable and the path height is kept.
			return !float.IsNaN(groundHeight)
			       && !float.IsInfinity(groundHeight)
			       && Mathf.Abs(groundHeight - position.y) <= MaximumTerrainSampleDelta;
		}

		private static bool IsSameHeightHeld()
		{
			return PluginConfig.PieceRepeaterLevelByDefault.Value
				|| ToolInput.WasModifierHeld(PluginConfig.PieceRepeaterLevelModifier.Value);
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

			// In the offset editor, let the real piece behind the preview remain
			// readable so small join and root corrections can be judged against it.
			if (_pieceGhostMaterial)
				_pieceGhostMaterial.color = _offsetEditor != null && _offsetEditor.IsOpen
					? new Color(0.42f, 0.62f, 0.68f, 0.14f)
					: new Color(0.35f, 0.82f, 0.9f, 0.28f);

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
					Placement initial = AnarchyModeActive
						? GetAnarchyInitialPlacement(_aim, metadata)
						: GetInitialPlacement(prefab, _aim, metadata);
					if (!AnarchyModeActive
					    && TryGetInitialSnapPlacement(Player.m_localPlayer, _aim, out Placement snappedPose))
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


