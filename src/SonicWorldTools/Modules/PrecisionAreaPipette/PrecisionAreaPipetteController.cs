using System.Collections.Generic;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using SonicWorldTools.Integration;
using SonicWorldTools.Refinement;
using UnityEngine;

namespace SonicWorldTools.Modules.PrecisionAreaPipette
{
	/// <summary>Tool lifecycle phases for Precision Area Pipette (M3).</summary>
	internal enum PrecisionAreaPipettePhase
	{
		Idle = 0,
		AwaitingCornerA = 1,
		AwaitingCornerB = 2,
		AwaitingCornerC = 3,
		Locked = 4,
	}

	/// <summary>
	/// Orchestrates precision area pipette: idle → A → B (width) → C (depth/height OBB) → locked,
	/// Locked-phase refinement (add/exclude), and M5 Infinity Hammer pick-up hand-off.
	/// </summary>
	internal sealed class PrecisionAreaPipetteController
	{
		private const float CandidateRefreshInterval = 0.05f;

		private const float QueryPadding = 2f;

		private static readonly List<Piece> PieceBuffer = new List<Piece>(256);

		private static readonly List<SelectionCandidate> CandidateBuffer = new List<SelectionCandidate>(256);

		internal static PrecisionAreaPipetteController Instance { get; private set; }

		private readonly SelectionState _state = new SelectionState();

		private readonly Dictionary<string, GameObject> _keyToRoot =
			new Dictionary<string, GameObject>(256);

		private Vector3? _cornerA;

		private Vector3? _cornerB;

		private Vector3? _cornerC;

		private float _candidateTimer;

		private string _hudLine = string.Empty;

		private string _hudNotice = string.Empty;

		private float _hudNoticeExpiresAt;

		private GUIStyle _hudTitleStyle;

		private GUIStyle _hudBodyStyle;

		private Texture2D _hudPanelTex;

		private GameObject _hoverRoot;

		/// <summary>
		/// After menu select / Activate, ignore SetCorner until Attack/LMB is released
		/// (same class of latch Dismantleheim uses — menu click must not count as Corner A,
		/// and the build UI must not steal the first world click).
		/// </summary>
		private bool _awaitPrimaryRelease;

		/// <summary>
		/// Frames left where !InPlaceMode is ignored (IH/BuildUi flickers place-mode on tool select
		/// and on the first Attack after Instant tools).
		/// </summary>
		private int _placeModeGraceFrames;

		/// <summary>Consecutive frames outside place mode before we deactivate.</summary>
		private int _notInPlaceFrames;

		/// <summary>
		/// After Escape cancels a selection, keep Menu.Show blocked for a couple frames so
		/// update-order cannot open the pause menu on the same keypress.
		/// </summary>
		private int _suppressPauseMenuFrames;

		/// <summary>True while the tool is armed (phases AwaitingCornerA+).</summary>
		internal bool IsActive { get; private set; }

		/// <summary>
		/// True when Escape should cancel pipette work instead of opening the pause menu
		/// (any phase past idle Corner A, or Corner A already placed).
		/// </summary>
		internal bool HasCancellableSelection
		{
			get
			{
				if (!IsActive)
				{
					return false;
				}

				if (Phase != PrecisionAreaPipettePhase.AwaitingCornerA)
				{
					return true;
				}

				return _cornerA.HasValue;
			}
		}

		/// <summary>Menu.Show should no-op while Escape is owned by pipette cancel.</summary>
		internal bool ShouldSuppressPauseMenu
		{
			get
			{
				if (!IsActive)
				{
					return false;
				}

				if (_suppressPauseMenuFrames > 0)
				{
					return true;
				}

				return HasCancellableSelection && PrecisionAreaPipetteInput.WasKeyDown(KeyCode.Escape);
			}
		}

		internal PrecisionAreaPipettePhase Phase { get; private set; }

		/// <summary>Frozen for M4: volume + base/added/excluded.</summary>
		internal SelectionState State => _state;

		/// <summary>Frozen for M4/M5: effective selection keys (ZDOID.ToString()).</summary>
		internal IReadOnlyCollection<string> GetEffectiveSelectionKeys()
		{
			return _state.GetEffectiveSelection();
		}

		internal Vector3? CornerA => _cornerA;

		internal Vector3? CornerB => _cornerB;

		internal Vector3? CornerC => _cornerC;

		internal PrecisionAreaPipetteController()
		{
			Instance = this;
			Phase = PrecisionAreaPipettePhase.Idle;
		}

		internal void Activate()
		{
			Activate("command");
		}

		internal void Activate(string source)
		{
			if (IsActive)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"Precision Area Pipette Activate(" + source + ") ignored — already active phase="
					+ Phase + ".");
				return;
			}

			// Do not Selection.Clear / strip the Hammer placement ghost here — IH keeps a
			// placement ghost whenever the Hammer is in place mode; clearing it NREs UpdatePlacementGhost.
			Player local = Player.m_localPlayer;
			bool place = (Object)(object)local != (Object)null && local.InPlaceMode();
			bool pieceMenu = false;
			try
			{
				pieceMenu = Hud.IsPieceSelectionVisible();
			}
			catch
			{
				// ignored
			}

			IsActive = true;
			_hudNotice = string.Empty;
			Phase = PrecisionAreaPipettePhase.AwaitingCornerA;
			_awaitPrimaryRelease = true;
			_placeModeGraceFrames = 180;
			_notInPlaceFrames = 0;
			TryHidePieceSelectionUi();
			ClearSelectionVisuals();
			UpdateHudText();
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"Precision Area Pipette active via " + source
				+ " place=" + place
				+ " pieceMenu=" + pieceMenu
				+ " grace=" + _placeModeGraceFrames
				+ " awaitRelease=" + _awaitPrimaryRelease + ".");
		}

		internal void Deactivate()
		{
			Deactivate("unspecified");
		}

		internal void Deactivate(string source)
		{
			if (!IsActive && Phase == PrecisionAreaPipettePhase.Idle)
			{
				return;
			}

			PrecisionAreaPipettePhase prev = Phase;
			bool hadA = _cornerA.HasValue;
			bool hadB = _cornerB.HasValue;
			bool hadC = _cornerC.HasValue;
			IsActive = false;
			_hudNotice = string.Empty;
			Phase = PrecisionAreaPipettePhase.Idle;
			_awaitPrimaryRelease = false;
			_placeModeGraceFrames = 0;
			_notInPlaceFrames = 0;
			// Clear corners, refinement, and preview so nothing sticks after tool switch / hand-off.
			ClearSelectionVisuals();
			_hudLine = string.Empty;
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"Precision Area Pipette inactive (" + source + ") wasPhase=" + prev
				+ " cornerA=" + hadA + " cornerB=" + hadB + " cornerC=" + hadC + ".");
		}

		/// <summary>
		/// Hand effective selection to Infinity Hammer as a multi-object copy ghost, then deactivate.
		/// </summary>
		internal bool TryPickUpSelection()
		{
			if (!IsActive || Phase != PrecisionAreaPipettePhase.Locked)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[SonicWorldTools] Pick-up ignored — lock a selection first.");
				return false;
			}

			HashSet<string> effective = _state.GetEffectiveSelection();
			if (effective.Count == 0)
			{
				_hudLine = "Precision Area Pipette\nNothing selected to pick up.";
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"[SonicWorldTools] Pick-up ignored — effective selection is empty.");
				return false;
			}

			if (PluginConfig.IsDryRunSelection())
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[SonicWorldTools] DryRunSelection: would hand off "
					+ effective.Count
					+ " object(s); skipping Infinity Hammer hand-off.");
				LogVerbose("Base selection = " + _state.BaseSelection.Count);
				LogVerbose("Added manually = " + _state.AddedSelection.Count);
				LogVerbose("Excluded manually = " + _state.ExcludedSelection.Count);
				LogVerbose("Effective selection = " + effective.Count);
				_hudLine = "Precision Area Pipette\nDry-run pick-up (" + effective.Count + " objects) — no hand-off.";
				return false;
			}

			bool ok = InfinityHammerBridge.TryHandOffCopy(
				effective,
				out List<ZNetView> resolvedViews,
				out List<string> droppedKeys,
				out bool disablePipette,
				out string userMessage);

			if (droppedKeys.Count > 0)
			{
				_state.DropKeys(droppedKeys);
				for (int i = 0; i < droppedKeys.Count; i++)
				{
					_keyToRoot.Remove(droppedKeys[i]);
				}

				LogVerbose(
					"Dropped unloaded before hand-off = " + droppedKeys.Count
					+ "; Effective selection = " + _state.GetEffectiveSelection().Count);
			}

			if (!ok)
			{
				if (!string.IsNullOrEmpty(userMessage))
				{
					_hudLine = userMessage;
					_hudNotice = userMessage;
					_hudNoticeExpiresAt = Time.unscaledTime + 6f;
				}

				if (disablePipette)
				{
					SonicWorldToolsPlugin.DisablePipetteDueToIntegrationFailure();
				}
				else if (string.IsNullOrEmpty(userMessage))
				{
					UpdateHudText();
				}

				return false;
			}

			LogVerbose("Hand-off views = " + resolvedViews.Count);
			Deactivate();
			return true;
		}

		/// <summary>Per-frame tick from <see cref="SonicWorldToolsPlugin"/>.</summary>
		internal void Tick()
		{
			if (!IsActive)
			{
				_suppressPauseMenuFrames = 0;
				return;
			}

			if (_suppressPauseMenuFrames > 0)
			{
				_suppressPauseMenuFrames--;
			}

			Player player = Player.m_localPlayer;
			if ((Object)(object)player == (Object)null)
			{
				return;
			}

			// §18.29: holster / leave place mode → deactivate, but debounce Instant flicker.
			// Instant tools often drop InPlaceMode for a beat after Attack (corner clicks).
			if (!player.InPlaceMode())
			{
				if (_placeModeGraceFrames > 0)
				{
					_placeModeGraceFrames--;
					DrawPreviewWithAim(null);
					UpdateHudText();
					return;
				}

				_notInPlaceFrames++;
				// Locked selections need longer patience — refine / pick-up after Corner C.
				int holdFrames = Phase == PrecisionAreaPipettePhase.Locked ? 90 : 20;
				if (_notInPlaceFrames < holdFrames)
				{
					DrawPreviewWithAim(null);
					UpdateHudText();
					return;
				}

				Deactivate("left-place-mode");
				return;
			}

			_notInPlaceFrames = 0;
			if (_placeModeGraceFrames > 0)
			{
				_placeModeGraceFrames--;
			}

			if (_awaitPrimaryRelease)
			{
				if (!PrecisionAreaPipetteInput.IsPrimaryCornerHeld())
				{
					_awaitPrimaryRelease = false;
				}
				else
				{
					// Still show live aim while waiting for menu-click release.
					DrawPreviewWithAim(null);
					UpdateHudText();
					return;
				}
			}

			PrecisionAreaPipetteInput.FrameInput input = PrecisionAreaPipetteInput.Sample();
			RefinementInput.FrameInput refine = default;
			if (Phase == PrecisionAreaPipettePhase.Locked)
			{
				refine = RefinementInput.Sample();
			}

			_hoverRoot = ResolveHoverRoot(refine);

			if (input.CancelDown)
			{
				if (HasCancellableSelection)
				{
					_suppressPauseMenuFrames = 3;
				}

				CancelSelection();
				return;
			}

			if (input.ResetCornersDown)
			{
				ResetCorners();
				return;
			}

			if (Phase == PrecisionAreaPipettePhase.Locked && input.ResetRefinementDown)
			{
				ResetRefinement();
				return;
			}

			if (Phase == PrecisionAreaPipettePhase.Locked && input.PickUpDown)
			{
				TryPickUpSelection();
				return;
			}

			if (Phase == PrecisionAreaPipettePhase.Locked
			    && PrecisionAreaPipetteInput.WasPermanentDeleteSelectionDown())
			{
				TryRemoveLockedSelection(SelectionRemoval.RemovalKind.PermanentDelete);
				return;
			}

			if (Phase == PrecisionAreaPipettePhase.Locked
			    && PrecisionAreaPipetteInput.WasDismantleSelectionDown())
			{
				TryRemoveLockedSelection(SelectionRemoval.RemovalKind.DismantleRefunds);
				return;
			}

			bool refineHandled = false;
			if (Phase == PrecisionAreaPipettePhase.Locked)
			{
				refineHandled = TryHandleRefinement(refine);
			}

			if (!refineHandled && input.SetCornerDown && input.HasAimPoint)
			{
				if (Phase == PrecisionAreaPipettePhase.Locked)
				{
					// Plain LMB while locked used to start "resize" (= wipe into a new A).
					// Reset corners is explicit (Home / Esc); keep the locked box intact.
					_hudLine = "Precision Area Pipette"
					           + FormatCountDimsLine()
					           + "\nLocked — Home resets corners, Esc cancels"
					           + "\n(LMB does not resize)";
				}
				else
				{
					OnSetCorner(input.AimPoint);
				}
			}
			else if (!refineHandled && input.HasAimPoint)
			{
				if (Phase == PrecisionAreaPipettePhase.AwaitingCornerB)
				{
					_cornerB = input.AimPoint;
				}
				else if (Phase == PrecisionAreaPipettePhase.AwaitingCornerC)
				{
					_cornerC = input.AimPoint;
				}
			}

			_candidateTimer -= Time.unscaledDeltaTime;
			bool refreshCandidates = _candidateTimer <= 0f;
			if (refreshCandidates)
			{
				_candidateTimer = CandidateRefreshInterval;
			}

			if (Phase == PrecisionAreaPipettePhase.AwaitingCornerC
			    && _cornerA.HasValue
			    && _cornerB.HasValue
			    && _cornerC.HasValue
			    && refreshCandidates)
			{
				RecomputeFromThreeCorners(_cornerA.Value, _cornerB.Value, _cornerC.Value);
			}
			else if (Phase == PrecisionAreaPipettePhase.Locked
			         && _state.HasCorners
			         && refreshCandidates)
			{
				// Keep key map warm for outside-box additions / hover without changing volume.
				RefreshCandidateMap(_state.Volume);
			}

			Vector3? aim = input.HasAimPoint ? input.AimPoint : (Vector3?)null;
			PrecisionAreaPipettePreview.Draw(
				Phase, _cornerA, _cornerB, _cornerC, _state, _keyToRoot, _hoverRoot, aim);
			PrecisionAreaPipettePreview.TickHighlights();
			UpdateHudText();
		}

		/// <summary>Sample aim + draw even when corner input is latched / place-mode grace.</summary>
		private void DrawPreviewWithAim(GameObject hoverRoot)
		{
			Vector3? aim = null;
			if (PrecisionAreaPipetteInput.TryGetAimPoint(out Vector3 point))
			{
				aim = point;
			}

			PrecisionAreaPipettePreview.Draw(
				Phase, _cornerA, _cornerB, _cornerC, _state, _keyToRoot, hoverRoot, aim);
			PrecisionAreaPipettePreview.TickHighlights();
		}

		internal void DrawHud()
		{
			if (!IsActive || string.IsNullOrEmpty(_hudLine))
			{
				return;
			}

			EnsureHudStyles();
			string hudText = _hudLine;
			if (!string.IsNullOrEmpty(_hudNotice) && Time.unscaledTime < _hudNoticeExpiresAt)
			{
				hudText += "\n" + _hudNotice;
			}
			else
			{
				_hudNotice = string.Empty;
			}

			const float margin = 14f;
			const float panelPad = 12f;
			const float width = 420f;
			string[] lines = hudText.Split('\n');
			float titleH = _hudTitleStyle.CalcHeight(new GUIContent(lines[0]), width - panelPad * 2f);
			float bodyH = 0f;
			for (int i = 1; i < lines.Length; i++)
			{
				bodyH += _hudBodyStyle.CalcHeight(new GUIContent(lines[i]), width - panelPad * 2f) + 2f;
			}

			float height = panelPad * 2f + titleH + bodyH + 4f;
			var panel = new Rect(margin, margin, width, height);

			Color prev = GUI.color;
			GUI.color = new Color(0.04f, 0.06f, 0.09f, 0.82f);
			GUI.DrawTexture(panel, _hudPanelTex);
			GUI.color = prev;

			float y = panel.y + panelPad;
			GUI.Label(
				new Rect(panel.x + panelPad, y, panel.width - panelPad * 2f, titleH),
				lines[0],
				_hudTitleStyle);
			y += titleH + 4f;
			for (int i = 1; i < lines.Length; i++)
			{
				float h = _hudBodyStyle.CalcHeight(new GUIContent(lines[i]), width - panelPad * 2f);
				GUI.Label(
					new Rect(panel.x + panelPad, y, panel.width - panelPad * 2f, h),
					lines[i],
					_hudBodyStyle);
				y += h + 2f;
			}
		}

		private void EnsureHudStyles()
		{
			if (_hudPanelTex == null)
			{
				_hudPanelTex = Texture2D.whiteTexture;
			}

			if (_hudTitleStyle == null)
			{
				_hudTitleStyle = new GUIStyle(GUI.skin.label)
				{
					fontSize = 17,
					fontStyle = FontStyle.Bold,
					wordWrap = true,
					normal = { textColor = new Color(0.95f, 0.97f, 1f, 1f) },
				};
			}

			if (_hudBodyStyle == null)
			{
				_hudBodyStyle = new GUIStyle(GUI.skin.label)
				{
					fontSize = 14,
					fontStyle = FontStyle.Normal,
					wordWrap = true,
					normal = { textColor = new Color(0.82f, 0.88f, 0.94f, 1f) },
				};
			}
		}

		internal static void ClearInstance()
		{
			if (Instance != null)
			{
				Instance.Deactivate();
				PrecisionAreaPipettePreview.Dispose();
				Instance = null;
			}
		}

		private bool TryHandleRefinement(RefinementInput.FrameInput refine)
		{
			if (!refine.AddDown && !refine.ExcludeDown)
			{
				return false;
			}

			// Consume the click so corner resize does not also fire under Alt.
			if (!refine.HasHoverPiece)
			{
				return true;
			}

			if (!TryGetPieceKey(refine.HoverPiece, out string key, out GameObject root))
			{
				return true;
			}

			_keyToRoot[key] = root;

			if (refine.AddDown)
			{
				if (_state.ContainsEffective(new SelectionObjectId(key)))
				{
					LogVerbose("Refine add ignored — already effective: " + key);
					return true;
				}

				_state.Add(new SelectionObjectId(key));
				LogVerbose("Added manually = " + _state.AddedSelection.Count
				           + "; Effective selection = " + _state.GetEffectiveSelection().Count);
				return true;
			}

			if (!_state.ContainsEffective(new SelectionObjectId(key)))
			{
				LogVerbose("Refine exclude ignored — not effective: " + key);
				return true;
			}

			_state.Exclude(new SelectionObjectId(key));
			LogVerbose("Excluded manually = " + _state.ExcludedSelection.Count
			           + "; Effective selection = " + _state.GetEffectiveSelection().Count);
			return true;
		}

		private GameObject ResolveHoverRoot(RefinementInput.FrameInput refine)
		{
			if (Phase != PrecisionAreaPipettePhase.Locked
			    && Phase != PrecisionAreaPipettePhase.AwaitingCornerC)
			{
				return null;
			}

			Piece hover = null;
			if (refine.HasHoverPiece)
			{
				hover = refine.HoverPiece;
			}
			else
			{
				Player player = Player.m_localPlayer;
				if ((Object)(object)player != (Object)null)
				{
					hover = player.GetHoveringPiece();
				}
			}

			if ((Object)(object)hover == (Object)null)
			{
				return null;
			}

			return hover.gameObject;
		}

		private void OnSetCorner(Vector3 aimPoint)
		{
			// Corner clicks can flicker place-mode for a beat after Attack — keep ownership.
			_placeModeGraceFrames = System.Math.Max(_placeModeGraceFrames, 180);
			_notInPlaceFrames = 0;

			switch (Phase)
			{
				case PrecisionAreaPipettePhase.AwaitingCornerA:
					_cornerA = aimPoint;
					_cornerB = null;
					_cornerC = null;
					// Persist add/exclude across a new volume (Home → A→B→C); Escape uses full Clear.
					if (IsPersistRefinementsEnabled())
					{
						_state.ClearVolumeKeepRefinements();
					}
					else
					{
						_state.Clear();
					}

					_keyToRoot.Clear();
					Phase = PrecisionAreaPipettePhase.AwaitingCornerB;
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
						"Precision Area Pipette Corner A = " + FormatVec(aimPoint));
					LogVerbose("Corner A = " + FormatVec(aimPoint));
					break;

				case PrecisionAreaPipettePhase.AwaitingCornerB:
					_cornerB = aimPoint;
					_cornerC = null;
					Phase = PrecisionAreaPipettePhase.AwaitingCornerC;
					SonicWorldToolsPlugin.ModLogger?.LogInfo(
						"Precision Area Pipette Corner B = " + FormatVec(aimPoint)
						+ " (width edge); set Corner C for depth/height.");
					LogVerbose("Corner B = " + FormatVec(aimPoint));
					break;

				case PrecisionAreaPipettePhase.AwaitingCornerC:
					_cornerC = aimPoint;
					RecomputeFromThreeCorners(_cornerA.Value, _cornerB.Value, _cornerC.Value);
					Phase = PrecisionAreaPipettePhase.Locked;
					LogSelectionLock(aimPoint);
					break;
			}
		}

		/// <summary>One-shot diagnostics so lock-time selection quality is visible in BepInEx log.</summary>
		private void LogSelectionLock(Vector3 cornerC)
		{
			SelectionVolume v = _state.Volume;
			int selected = _state.GetEffectiveSelection().Count;
			int candidates = CandidateBuffer.Count;
			int mapped = _keyToRoot.Count;
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"Precision Area Pipette Corner C = " + FormatVec(cornerC)
				+ "; selected = " + selected
				+ " / candidates = " + candidates
				+ " (mapped roots = " + mapped + ")"
				+ "; oriented = " + v.IsOriented
				+ "; size = "
				+ v.SizeX.ToString("0.0") + "m × "
				+ v.SizeZ.ToString("0.0") + "m × "
				+ v.SizeY.ToString("0.0") + "m"
				+ " (W×D×H)");

			// Sample a few selected piece names for eyeballing false positives/negatives.
			int samples = 0;
			foreach (string key in _state.BaseSelection)
			{
				if (!_keyToRoot.TryGetValue(key, out GameObject root)
				    || (Object)(object)root == (Object)null)
				{
					continue;
				}

				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[SonicWorldTools] Selected sample: " + root.name);
				samples++;
				if (samples >= 8)
				{
					break;
				}
			}

			LogVerbose("Corner C = " + FormatVec(cornerC));
			LogVerbose("Base selection = " + _state.BaseSelection.Count);
			LogVerbose("Added manually = " + _state.AddedSelection.Count);
			LogVerbose("Excluded manually = " + _state.ExcludedSelection.Count);
			LogVerbose("Effective selection = " + selected);
		}

		private void TryRemoveLockedSelection(SelectionRemoval.RemovalKind kind)
		{
			if (!IsActive || Phase != PrecisionAreaPipettePhase.Locked || !_state.HasCorners)
			{
				return;
			}

			HashSet<string> effective = _state.GetEffectiveSelection();
			if (effective.Count == 0)
			{
				_hudLine = "Precision Area Pipette\nNothing selected to remove.";
				return;
			}

			bool ok = SelectionRemoval.TryRemoveEffectiveSelection(
				effective,
				_keyToRoot,
				kind,
				out int requested,
				out int skipped,
				out string summary);

			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[SonicWorldTools] " + summary
				+ (WorldEditUndoBridge.IsAvailable && !PluginConfig.IsDryRunSelection()
					? " (WEC undo)"
					: string.Empty));

			if (!ok && requested == 0)
			{
				_hudLine = "Precision Area Pipette\n" + summary;
				return;
			}

			// Selection targets are gone / pending destroy — clear box and re-arm for a new A.
			ClearSelectionVisuals();
			Phase = PrecisionAreaPipettePhase.AwaitingCornerA;
			string undoHint = WorldEditUndoBridge.IsAvailable && !PluginConfig.IsDryRunSelection()
				? "\nUndo: IH/WEC undo bind (often Keypad7)"
				: string.Empty;
			_hudLine = "Precision Area Pipette\n" + summary + undoHint
			           + "\nSet Corner A";
			if (skipped > 0)
			{
				LogVerbose("Removal skipped = " + skipped);
			}
		}

		/// <summary>Escape / CancelKey: clear corners, refinement, preview; stay armed.</summary>
		private void CancelSelection()
		{
			ClearSelectionVisuals();
			Phase = PrecisionAreaPipettePhase.AwaitingCornerA;
			_hudLine = "Precision Area Pipette — set Corner A";
			SonicWorldToolsPlugin.ModLogger?.LogInfo("Precision Area Pipette selection cancelled.");
		}

		/// <summary>
		/// ResetCornersKey (Home): clear volume and return to Corner A.
		/// Keeps add/exclude when PersistRefinementsAfterResize is true (S18); Escape still full-clears.
		/// </summary>
		private void ResetCorners()
		{
			ClearSelectionVisuals(keepRefinements: IsPersistRefinementsEnabled());
			Phase = PrecisionAreaPipettePhase.AwaitingCornerA;
			_hudLine = IsPersistRefinementsEnabled()
				? "Precision Area Pipette — corners reset (refinements kept); set Corner A"
				: "Precision Area Pipette — corners reset; set Corner A";
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"Precision Area Pipette corners reset"
				+ (IsPersistRefinementsEnabled() ? " (refinements kept)." : "."));
		}

		private static bool IsPersistRefinementsEnabled()
		{
			return PluginConfig.PersistRefinementsAfterResize == null
			       || PluginConfig.PersistRefinementsAfterResize.Value;
		}

		/// <summary>ResetRefinementKey: clear add/exclude only while Locked.</summary>
		private void ResetRefinement()
		{
			if (Phase != PrecisionAreaPipettePhase.Locked || !_state.HasCorners)
			{
				return;
			}

			_state.ResetRefinement();
			LogVerbose("Added manually = 0");
			LogVerbose("Excluded manually = 0");
			LogVerbose("Effective selection = " + _state.GetEffectiveSelection().Count);
			SonicWorldToolsPlugin.ModLogger?.LogInfo("Precision Area Pipette refinement reset.");
			UpdateHudText();
		}

		private void ClearSelectionVisuals(bool keepRefinements = false)
		{
			_cornerA = null;
			_cornerB = null;
			_cornerC = null;
			if (keepRefinements)
			{
				_state.ClearVolumeKeepRefinements();
			}
			else
			{
				_state.Clear();
			}

			_keyToRoot.Clear();
			_candidateTimer = 0f;
			_hoverRoot = null;
			PrecisionAreaPipettePreview.Clear();
		}

		private void RecomputeFromThreeCorners(Vector3 a, Vector3 b, Vector3 c)
		{
			SelectionVolume volume = SelectionVolume.FromThreePoints(a, b, c)
				.Expanded(PluginConfig.GetSelectionPadding());
			CollectCandidates(volume);
			// Outside-box adds/excludes must stay in the candidate key set or persist prune drops them.
			AppendRefinementPiecesToCandidates();
			bool persist = IsPersistRefinementsEnabled();
			_state.SetThreeCorners(
				a.x,
				a.y,
				a.z,
				b.x,
				b.y,
				b.z,
				c.x,
				c.y,
				c.z,
				CandidateBuffer,
				persist);
			EnsureRefinementRootsMapped();
		}

		private void AppendRefinementPiecesToCandidates()
		{
			AppendKeysAsCandidates(_state.AddedSelection);
			AppendKeysAsCandidates(_state.ExcludedSelection);
		}

		private void AppendKeysAsCandidates(IReadOnlyCollection<string> keys)
		{
			if (keys == null || keys.Count == 0)
			{
				return;
			}

			foreach (string key in keys)
			{
				if (string.IsNullOrEmpty(key) || _keyToRoot.ContainsKey(key))
				{
					// Already in CandidateBuffer from CollectCandidates (same map).
					continue;
				}

				if (!TryResolveKeyToRoot(key, out GameObject root) || (Object)(object)root == (Object)null)
				{
					continue;
				}

				ZNetView view = root.GetComponent<ZNetView>();
				if ((Object)(object)view == (Object)null || !view.IsValid())
				{
					continue;
				}

				ZDO zdo = view.GetZDO();
				if (zdo == null)
				{
					continue;
				}

				Vector3 raw = zdo.GetPosition();
				Vector3 pos = WorldEditBridge.GetCanonicalWorldPosition(raw);
				CandidateBuffer.Add(new SelectionCandidate(new SelectionObjectId(key), pos.x, pos.y, pos.z));
				_keyToRoot[key] = root;
			}
		}

		/// <summary>Refresh piece map without recomputing base (Locked keep-alive).</summary>
		private void RefreshCandidateMap(SelectionVolume volume)
		{
			CollectCandidates(volume);
			EnsureRefinementRootsMapped();
		}

		private void CollectCandidates(SelectionVolume volume)
		{
			CandidateBuffer.Clear();
			_keyToRoot.Clear();
			PieceBuffer.Clear();

			Vector3 center = (volume.Min + volume.Max) * 0.5f;
			Vector3 aabbSize = volume.Max - volume.Min;
			float radius = aabbSize.magnitude * 0.5f + QueryPadding;
			if (radius < QueryPadding)
			{
				radius = QueryPadding;
			}

			Piece.GetAllPiecesInRadius(center, radius, PieceBuffer);

			for (int i = 0; i < PieceBuffer.Count; i++)
			{
				Piece piece = PieceBuffer[i];
				if ((Object)(object)piece == (Object)null)
				{
					continue;
				}

				ZNetView view = piece.GetComponent<ZNetView>();
				if ((Object)(object)view == (Object)null || !view.IsValid())
				{
					continue;
				}

				ZDO zdo = view.GetZDO();
				if (zdo == null)
				{
					continue;
				}

				Vector3 raw = zdo.GetPosition();
				Vector3 pos = WorldEditBridge.GetCanonicalWorldPosition(raw);
				string key = zdo.m_uid.ToString();
				CandidateBuffer.Add(new SelectionCandidate(new SelectionObjectId(key), pos.x, pos.y, pos.z));
				_keyToRoot[key] = piece.gameObject;
			}
		}

		/// <summary>
		/// Ensure manually added / excluded pieces outside the volume radius stay in <see cref="_keyToRoot"/>.
		/// </summary>
		private void EnsureRefinementRootsMapped()
		{
			MapMissingKeys(_state.AddedSelection);
			MapMissingKeys(_state.ExcludedSelection);
		}

		private void MapMissingKeys(IReadOnlyCollection<string> keys)
		{
			if (keys == null || keys.Count == 0)
			{
				return;
			}

			foreach (string key in keys)
			{
				if (_keyToRoot.ContainsKey(key))
				{
					continue;
				}

				if (!TryResolveKeyToRoot(key, out GameObject root))
				{
					continue;
				}

				_keyToRoot[key] = root;
			}
		}

		private static bool TryResolveKeyToRoot(string key, out GameObject root)
		{
			root = null;
			if (string.IsNullOrEmpty(key))
			{
				return false;
			}

			ZNetScene scene = ZNetScene.instance;
			if ((Object)(object)scene == (Object)null)
			{
				return false;
			}

			if (!InfinityHammerBridge.TryParseZdoid(key, out ZDOID zdoid) || zdoid.IsNone())
			{
				return false;
			}

			GameObject go = scene.FindInstance(zdoid);
			if ((Object)(object)go == (Object)null)
			{
				return false;
			}

			root = go;
			return true;
		}

		private static bool TryGetPieceKey(Piece piece, out string key, out GameObject root)
		{
			key = null;
			root = null;
			if ((Object)(object)piece == (Object)null)
			{
				return false;
			}

			ZNetView view = piece.GetComponent<ZNetView>();
			if ((Object)(object)view == (Object)null || !view.IsValid())
			{
				return false;
			}

			ZDO zdo = view.GetZDO();
			if (zdo == null)
			{
				return false;
			}

			key = zdo.m_uid.ToString();
			root = piece.gameObject;
			return !string.IsNullOrEmpty(key);
		}

		private void UpdateHudText()
		{
			string tool = "Precision Area Pipette";
			string setKey = PluginConfig.FormatKeyLabel(
				PluginConfig.SetCornerKey != null ? PluginConfig.SetCornerKey.Value : KeyCode.Mouse0);
			string addKey = PluginConfig.FormatKeyLabel(
				PluginConfig.AddPieceKey != null ? PluginConfig.AddPieceKey.Value : KeyCode.Mouse0);
			string excludeKey = PluginConfig.FormatKeyLabel(
				PluginConfig.ExcludePieceKey != null ? PluginConfig.ExcludePieceKey.Value : KeyCode.Mouse2);
			string pickKey = PluginConfig.FormatPickUpChordLabel();
			string cancelKey = PluginConfig.FormatKeyLabel(
				PluginConfig.CancelKey != null ? PluginConfig.CancelKey.Value : KeyCode.Escape);
			string resetRefineKey = PluginConfig.FormatKeyLabel(
				PluginConfig.ResetRefinementKey != null
					? PluginConfig.ResetRefinementKey.Value
					: KeyCode.Backspace);
			string resetCornersKey = PluginConfig.FormatKeyLabel(
				PluginConfig.ResetCornersKey != null ? PluginConfig.ResetCornersKey.Value : KeyCode.Home);
			string dismantleKey = FormatRemovalChord(
				PluginConfig.DismantleModifierKey != null
					? PluginConfig.DismantleModifierKey.Value
					: KeyCode.LeftControl,
				PluginConfig.SelectionRemovalKey != null
					? PluginConfig.SelectionRemovalKey.Value
					: KeyCode.Delete);
			string deleteKey = FormatRemovalChord(
				PluginConfig.PermanentDeleteModifierKey != null
					? PluginConfig.PermanentDeleteModifierKey.Value
					: KeyCode.LeftShift,
				PluginConfig.SelectionRemovalKey != null
					? PluginConfig.SelectionRemovalKey.Value
					: KeyCode.Delete);
			KeyCode modCode = PluginConfig.RefineModifierKey != null
				? PluginConfig.RefineModifierKey.Value
				: KeyCode.LeftAlt;
			string modPrefix = modCode == KeyCode.None
				? string.Empty
				: PluginConfig.FormatKeyLabel(modCode) + "+";

			switch (Phase)
			{
				case PrecisionAreaPipettePhase.AwaitingCornerA:
					_hudLine = tool + "\nClick " + setKey + " — set Corner A\n" + cancelKey + " cancel";
					return;

				case PrecisionAreaPipettePhase.AwaitingCornerB:
					_hudLine = tool
					           + "\nClick "
					           + setKey
					           + " — Corner B (width end)\n"
					           + cancelKey
					           + " cancel";
					return;

				case PrecisionAreaPipettePhase.AwaitingCornerC:
					_hudLine = tool
					           + "\nClick "
					           + setKey
					           + " — Corner C (depth / height)"
					           + FormatCountDimsLine()
					           + "\n"
					           + cancelKey
					           + " cancel";
					return;

				case PrecisionAreaPipettePhase.Locked:
					_hudLine = tool
					           + FormatCountDimsLine()
					           + "\n"
					           + pickKey
					           + "  copy   "
					           + dismantleKey
					           + "  dismantle   "
					           + deleteKey
					           + "  delete"
					           + "\n"
					           + modPrefix
					           + addKey
					           + " add   "
					           + modPrefix
					           + excludeKey
					           + " exclude"
					           + "\n"
					           + resetRefineKey
					           + " clear refine   "
					           + resetCornersKey
					           + " new box   "
					           + cancelKey
					           + " cancel";
					return;

				default:
					_hudLine = string.Empty;
					return;
			}
		}

		private static string FormatRemovalChord(KeyCode modifier, KeyCode key)
		{
			return PluginConfig.FormatKeyLabel(modifier) + "+" + PluginConfig.FormatKeyLabel(key);
		}

		private string FormatCountDimsLine()
		{
			if (!_state.HasCorners)
			{
				return string.Empty;
			}

			bool showCount = PluginConfig.ShowSelectedCount == null || PluginConfig.ShowSelectedCount.Value;
			bool showDims = PluginConfig.ShowDimensions == null || PluginConfig.ShowDimensions.Value;
			if (!showCount && !showDims)
			{
				return string.Empty;
			}

			string line = "\n";
			if (showCount)
			{
				int count = _state.GetEffectiveSelection().Count;
				line += count + " selected";
				int added = _state.AddedSelection.Count;
				int excluded = _state.ExcludedSelection.Count;
				if (added > 0 || excluded > 0)
				{
					line += "  (+" + added + " / −" + excluded + ")";
				}
			}

			if (showDims)
			{
				SelectionVolume v = _state.Volume;
				if (showCount)
				{
					line += "  ·  ";
				}

				// W × D × H
				line += v.SizeX.ToString("0.0") + " × "
				        + v.SizeZ.ToString("0.0") + " × "
				        + v.SizeY.ToString("0.0") + " m";
			}

			return line;
		}

		private static string FormatVec(Vector3 v)
		{
			return v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0");
		}

		private static void TryHidePieceSelectionUi()
		{
			try
			{
				if (Hud.IsPieceSelectionVisible())
				{
					Hud.HidePieceSelection();
				}
			}
			catch
			{
				// Hud may be unavailable during load / logout.
			}
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
