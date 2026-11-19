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
	internal sealed partial class PieceRepeaterController : IToolSession
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
		private const int MaximumPlacements = 2048;
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
		private Vector3 _aim;
		private float _sampledYaw;
		private IDisposable _restoreIhShapeShortcut;
		private bool _hasAim;
		private GameObject _previewRoot;
		private LineRenderer _outlineLine;
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
			controller.CaptureTraceTarget();
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
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			_snapPointIndex = -1;
			_gapMeters = Mathf.Clamp(PluginConfig.PieceRepeaterGap.Value, -5f, 5f);
			_rotationSteps = 0;
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
			_traceRows.Clear();
			_traceAnchor = null;
			_tracePendingStart = null;
			_traceActive = false;
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
				else if (HasTraceBase && ToolInput.WasModifierHeld(KeyCode.LeftShift) && ToolInput.WasModifierHeld(KeyCode.LeftAlt))
				{
					AdjustTraceRows(wheel > 0f ? 1 : -1);
				}
				else if (IsTracing && ToolInput.WasModifierHeld(KeyCode.LeftShift))
				{
					NudgeTraceOffset(Mathf.Sign(wheel) * TraceOffsetStep);
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

			// Next row (Shift + MMB) is checked before the trace chord: "either Alt" counts for the trace
			// modifier, so Shift + Alt + MMB would otherwise start a trace instead of stepping the row.
			if (!_shapeToolMode && IsNextRowChordDown())
			{
				HandleNextRowChord();
				return;
			}

			// The trace chord (Right Alt + MMB by default) is checked first so it wins over plain MMB sampling.
			if (!_shapeToolMode && IsTraceChordDown())
			{
				HandleTraceChord(player);
				return;
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
				if (_traceActive && _points.Count == 0) _traceActive = false;
				if (IsTracing)
				{
					if (finish) BuildAndPlace(player);
					else Notify("Traced row ready: " + PlaceChordLabel + " places it; Shift+wheel nudges the offset.");
					return;
				}
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
			// Shape outlines of edge-chain pieces use the shared solver so neighbouring tiles join at the corners.
			if (_mode == PieceRepeaterMode.Circle && _shapeFillMode == ShapeFillMode.Outline && _points.Count > 0 && _hasAim
			    && metadata.ProfileType == RepeatProfileType.EdgeChain)
			{
				GenerateEdgeChainCircle(prefab, metadata);
				return;
			}
			GenerateLayoutCore(prefab);
			for (int i = 0; i < _placements.Count; i++)
			{
				float groundOffset = -metadata.BottomOffset;
				_placements[i] = AdjustPlacement(_placements[i], metadata, groundOffset,
					_points.Count > 0 ? _points[0].y : _placements[i].Position.y);
			}
			if (_mode == PieceRepeaterMode.Path && _hasLockedStartPlacement && _placements.Count > 0)
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
			// Shape tiles that follow the terrain sample the ground directly. The build-piece surface lookup
			// can pull a tile onto a neighbouring piece's height instead of the ground it sits on.
			if (_shapeToolMode && !IsSameHeightHeld())
			{
				if (TryGetGroundHeight(rootPosition, out float groundHeight)) rootPosition.y = groundHeight + groundOffset;
				else if (ZoneSystem.instance && ZoneSystem.instance.GetGroundHeight(rootPosition + Vector3.up * 2f, out float sampled) && !float.IsNaN(sampled))
					rootPosition.y = sampled + groundOffset;
				else rootPosition.y += groundOffset;
			}
			else rootPosition = ApplySurfaceHeightMode(rootPosition, groundOffset, fixedHeight);
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
				GeneratePath(spacing);
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

		private float GetSpacing(GameObject prefab = null)
		{
			if (!prefab && ZNetScene.instance && !string.IsNullOrEmpty(_prefabId))
				prefab = ZNetScene.instance.GetPrefab(_prefabId);
			return Mathf.Max(MinimumSpacing, ResolvePlacementMetadata(prefab).Spacing + _gapMeters);
		}

		private void GenerateLayoutWithAim(GameObject prefab, Vector3 start, Vector3 aim)
		{
			Vector3 savedAim = _aim;
			_aim = aim;
			GenerateLayout(prefab);
			_aim = savedAim;
		}

		private static void Notify(string text)
		{
			// Centre messages replace each other immediately. The top-left slot is a queue that backs up
			// behind undo and spawn messages from WEC/IH, so Line Tool notices arrived late.
			if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text, 0, null);
		}
	}
}


