using System.Collections.Generic;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>
	/// Trace wall: follow an existing chain of placed pieces and lay the sampled piece in a
	/// parallel row on the player's side (e.g. floor tiles along the inside of a curved wall).
	/// The traced curve becomes fixed waypoints, so preview, height modes and placement are the
	/// normal Line Tool path flow.
	/// </summary>
	internal sealed partial class PieceRepeaterController
	{
		private const float TraceJoinTolerance = 0.35f;
		private const float TraceSearchRadius = 128f;
		private const int TraceMaxPieces = 256;
		private const float TraceOffsetStep = 0.1f;
		/// <summary>Bends sharper than this get an extra tile on the vertex to cover the wedge.</summary>
		private const float TraceCornerDegrees = 3f;

		private bool _traceActive;
		private readonly List<Vector3> _traceBase = new List<Vector3>();
		private int _traceSide = 1;
		private float _traceOffset;
		/// <summary>Offset of the first row, so Shift + Alt + wheel down cannot remove the row beside the wall.</summary>
		private float _traceFirstOffset;
		private float _lastTraceRowStepTime = -10f;
		/// <summary>How many rows are laid, from the wall outwards; each sits one piece depth beyond the last.</summary>
		private int _traceRowCount = 1;
		/// <summary>The rows laid for this trace, from the one beside the wall outwards. Each new row is traced from the one before it.</summary>
		private readonly List<List<Vector3>> _traceRows = new List<List<Vector3>>();
		/// <summary>The last row placed for this wall. New rows continue from it, so placed rows are not laid again.</summary>
		private List<Vector3> _traceAnchor;
		private float _traceRowStep;
		/// <summary>True when the trace was started on a wall's top face: the row then runs on top of the wall.</summary>
		private bool _traceOnTop;
		/// <summary>Waypoint Y that puts the row piece's top surface flush with the traced course's base.</summary>
		private float _traceRowY;
		private string _traceWallPrefab;
		private Piece _traceTarget;
		/// <summary>First press of the trace chord: the wall piece (and aimed point/face) the stretch starts at.</summary>
		private Piece _tracePendingStart;
		private Vector3 _tracePendingPoint;
		private Vector3 _tracePendingNormal = Vector3.up;
		private bool _traceLastOk;
		private Vector3 _traceAimPoint;
		private Vector3 _traceAimNormal = Vector3.up;
		private const float TraceAimDistance = 60f;
		/// <summary>Wall faces steeper than this count as "side faces" that decide which side the row goes on.</summary>
		private const float TraceSideFaceMaxNormalY = 0.7f;

		/// <summary>True while the current path is a traced row waiting to be placed.</summary>
		private bool IsTracing => _traceActive && _points.Count > 0;

		internal bool HasTraceBase => _traceBase.Count >= 2;
		internal float TraceOffset => _traceOffset;
		internal string TraceTargetLabel => _traceTarget ? Utils.GetPrefabName(_traceTarget.gameObject) : null;

		/// <summary>
		/// Two presses: the first marks where the stretch starts, the second where it ends. Following a
		/// wall until it stops can run onto parts the player did not mean, so the range is explicit.
		/// Pressing with nothing aimed at cancels a pending start.
		/// </summary>
		private void HandleTraceChord(Player player)
		{
			if (!TryGetAimedPiece(player, out Piece piece, out Vector3 point, out Vector3 normal))
			{
				if (_tracePendingStart)
				{
					_tracePendingStart = null;
					Notify("Trace start cleared");
				}
				else Notify("Trace wall: aim at a wall piece");
				return;
			}

			if (!_tracePendingStart)
			{
				_tracePendingStart = piece;
				_tracePendingPoint = point;
				_tracePendingNormal = normal;
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace start: " + Utils.GetPrefabName(piece.gameObject) + " at "
					+ piece.transform.position.ToString("F1") + " aimed " + point.ToString("F1") + ".");
				Notify("Trace start: " + Utils.GetPrefabName(piece.gameObject) + ". Aim at the end and press again");
				return;
			}

			_traceLastOk = false;
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace end: " + Utils.GetPrefabName(piece.gameObject) + " at "
				+ piece.transform.position.ToString("F1") + ".");
			TraceWall(_tracePendingStart, _tracePendingPoint, _tracePendingNormal, piece);
			// Keep the start when the end could not be reached, so a second try only needs the end.
			if (_traceLastOk) _tracePendingStart = null;
		}

		private static bool IsTraceChordDown()
		{
			KeyCode key = PluginConfig.PieceRepeaterTraceKey != null ? PluginConfig.PieceRepeaterTraceKey.Value : KeyCode.None;
			if (key == KeyCode.None) return false;
			KeyCode modifier = PluginConfig.PieceRepeaterTraceModifier != null ? PluginConfig.PieceRepeaterTraceModifier.Value : KeyCode.None;
			if (modifier != KeyCode.None && !ToolInput.WasModifierHeld(modifier)) return false;
			return ToolInput.WasBoundKeyOrMouseDown(key);
		}

		private static bool IsNextRowChordDown()
		{
			KeyCode key = PluginConfig.PieceRepeaterNextRowKey != null ? PluginConfig.PieceRepeaterNextRowKey.Value : KeyCode.None;
			if (key == KeyCode.None) return false;
			KeyCode modifier = PluginConfig.PieceRepeaterNextRowModifier != null ? PluginConfig.PieceRepeaterNextRowModifier.Value : KeyCode.None;
			if (modifier != KeyCode.None && !ToolInput.WasModifierHeld(modifier)) return false;
			return ToolInput.WasBoundKeyOrMouseDown(key);
		}

		/// <summary>Remembers the piece, point and face under the crosshair when the settings panel opens.</summary>
		internal void CaptureTraceTarget()
		{
			Player player = Player.m_localPlayer;
			_traceTarget = null;
			if (!player || !TryGetAimedPiece(player, out Piece piece, out Vector3 point, out Vector3 normal)) return;
			_traceTarget = piece;
			_traceAimPoint = point;
			_traceAimNormal = normal;
		}

		/// <summary>
		/// The piece straight under the crosshair, from our own camera ray. Player.GetHoveringPiece can
		/// pick a different piece than the one aimed at (it favours the build-ghost raycast), which
		/// started traces from the wrong wall piece or course.
		/// </summary>
		private static bool TryGetAimedPiece(Player player, out Piece piece, out Vector3 point, out Vector3 normal)
		{
			piece = null;
			point = Vector3.zero;
			normal = Vector3.up;
			GameCamera camera = GameCamera.instance;
			if (!camera) return false;
			RaycastHit[] hits = Physics.RaycastAll(new Ray(camera.transform.position, camera.transform.forward),
				TraceAimDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			foreach (RaycastHit hit in hits)
			{
				if (!hit.collider || hit.collider.GetComponentInParent<Player>() == player) continue;
				Piece hitPiece = hit.collider.GetComponentInParent<Piece>();
				if (!hitPiece) return false; // something that is not a piece (terrain, tree) is in front
				piece = hitPiece;
				point = hit.point;
				normal = hit.normal;
				return true;
			}
			return false;
		}

		internal void TraceCapturedWall()
		{
			if (!_traceTarget)
			{
				Notify("Trace wall: look at a wall piece when you open the settings, then press Trace wall.");
				return;
			}
			TraceWall(_traceTarget, _traceAimPoint, _traceAimNormal);
		}

		/// <summary>
		/// Shift+MMB: with a pending start it cancels that start (so a new wall can be picked); otherwise it steps
		/// the traced row out, like the Next row button.
		/// </summary>
		private void HandleNextRowChord()
		{
			if (_tracePendingStart)
			{
				_tracePendingStart = null;
				ClearTracedRow();
				Notify("Trace cleared. Aim at a new start and press Right Alt + MMB.");
				return;
			}
			if (HasTraceBase) TraceNextRow();
			else Notify("Trace a wall first (Right Alt + MMB, then again at the end).");
		}

		/// <summary>Drops the traced wall and its unplaced row, and removes the row's preview.</summary>
		private void ClearTracedRow()
		{
			_traceBase.Clear();
			_traceAnchor = null;
			_traceActive = false;
			_traceRows.Clear();
			_points.Clear();
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			UpdatePreview();
		}

		/// <summary>Steps the traced row out by one piece depth from the last traced wall.</summary>
		internal void TraceNextRow()
		{
			if (!HasTraceBase)
			{
				Notify("Next row: trace a wall first.");
				return;
			}
			if (!TryGetTracePiece(out _, out _)) return;
			_traceRowCount++;
			_traceOffset = _traceFirstOffset + (_traceRowCount - 1) * _traceRowStep;
			ApplyTrace("Next row");
		}

		/// <summary>Shift + Alt + wheel: wheel up adds a row, wheel down removes the last one (never the first).</summary>
		internal void AdjustTraceRows(int direction)
		{
			if (!HasTraceBase) return;
			// The scroll axis stays non-zero for several frames per notch, so one row per short interval.
			if (Time.unscaledTime - _lastTraceRowStepTime < 0.3f) return;
			_lastTraceRowStepTime = Time.unscaledTime;
			int current = _traceRows.Count;
			int count = direction > 0 ? current + 1 : Mathf.Max(0, current - 1);
			if (count == 0) return;
			RebuildTraceRows(count);
			UpdatePreview();
			Notify((direction > 0 ? "More rows" : "Fewer rows") + ": " + count + " row(s) at " + _traceOffset.ToString("0.##") + " m spacing (Shift+wheel changes spacing, " + PlaceChordLabel + " places)");
		}

		/// <summary>Moves the traced row to the other side of the wall.</summary>
		internal void FlipTraceSide()
		{
			if (!HasTraceBase)
			{
				Notify("Flip side: trace a wall first.");
				return;
			}
			if (!TryGetTracePiece(out _, out _)) return;
			_traceSide = -_traceSide;
			ApplyTrace("Flipped side");
		}

		internal void NudgeTraceOffset(float delta)
		{
			if (!HasTraceBase) return;
			_traceOffset = Mathf.Max(0f, Mathf.Round((_traceOffset + delta) * 100f) / 100f);
			if (_traceRows.Count > 1)
			{
				// Shift+wheel sets the spacing between rows, so every laid row moves with it.
				RebuildTraceRows(_traceRows.Count);
				UpdatePreview();
				return;
			}
			ApplyTrace(null);
		}

		private List<Vector3> OffsetRow(List<Vector3> row)
		{
			return LineTrace.Offset(row, _traceSide * _traceOffset).ConvertAll(p => new Vector3(p.x, _traceRowY, p.z));
		}

		/// <summary>
		/// Lays <paramref name="count"/> rows from the wall outwards. The first sits one offset from the wall, and each
		/// further row is traced from the one before it, one offset further out, so every gap equals the offset.
		/// </summary>
		private void RebuildTraceRows(int count)
		{
			_traceRows.Clear();
			// Rows continue from the last placed row when there is one, otherwise they start at the wall.
			List<Vector3> previous = _traceAnchor;
			for (int i = 0; i < count; i++)
			{
				List<Vector3> row = previous == null ? TracedRowPoints(_traceOffset) : OffsetRow(previous);
				_traceRows.Add(row);
				previous = row;
			}
			_points.Clear();
			_points.AddRange(_traceRows[_traceRows.Count - 1]);
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			_traceActive = true;
		}

		/// <summary>Traces the wall course through <paramref name="wall"/>; with <paramref name="endPiece"/> only the stretch from wall to endPiece.</summary>
		private void TraceWall(Piece wall, Vector3 aimPoint, Vector3 aimNormal, Piece endPiece = null)
		{
			if (!TryGetTracePiece(out GameObject rowPrefab, out ResolvedPrefabPlacement rowMetadata))
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace stopped before tracing: sampled piece '" + (_prefabId ?? "none") + "' is not usable (see the centre message).");
				return;
			}
			string wallPrefabName = Utils.GetPrefabName(wall.gameObject);
			GameObject wallPrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(wallPrefabName) : null;
			if (!wallPrefab)
			{
				Notify("Trace wall: '" + wallPrefabName + "' is not a known piece.");
				return;
			}

			ResolvedPrefabPlacement wallMetadata = PrefabPlacementMetadata.Resolve(wallPrefabName, wallPrefab);
			Bounds wallBounds = PrefabPlacementMetadata.GetLocalRendererBounds(wallPrefab);
			bool wallAlongX = RepeatsAlongX(wallMetadata, wallBounds);

			var segments = new List<TraceSegment>();
			int startIndex = -1;
			int endIndex = -1;
			Vector3 origin = wall.transform.position;
			foreach (WearNTear instance in WearNTear.GetAllInstances())
			{
				if (!instance) continue;
				Transform t = instance.transform;
				if ((t.position - origin).sqrMagnitude > TraceSearchRadius * TraceSearchRadius) continue;
				if (!string.Equals(Utils.GetPrefabName(instance.gameObject), wallPrefabName, System.StringComparison.OrdinalIgnoreCase)) continue;
				if (instance.gameObject == wall.gameObject) startIndex = segments.Count;
				if (endPiece && instance.gameObject == endPiece.gameObject) endIndex = segments.Count;
				segments.Add(PieceEnds(t, wallBounds, wallAlongX, wallMetadata));
			}
			if (startIndex < 0)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace start not found among " + wallPrefabName + " pieces (pieces found=" + segments.Count + ").");
				Notify("Trace wall: that piece is not a structural piece the tool can follow.");
				return;
			}

			// Stay on the course the player aimed at: stacked rows share ends in XZ but sit a full piece height apart.
			// Courses offset by half a piece (a 1 m step on a 2 m piece) still belong to one wall; a full piece
			// above or below does not.
			float heightTolerance = Mathf.Max(0.25f, wallBounds.size.y * Mathf.Abs(wall.transform.lossyScale.y) * 0.75f);
			int used;
			List<Vector3> polyline;
			if (endPiece)
			{
				if (endIndex < 0)
				{
					SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace end not found among " + wallPrefabName + " pieces (end piece=" + (endPiece ? Utils.GetPrefabName(endPiece.gameObject) : "none") + ", pieces found=" + segments.Count + ").");
					Notify("Trace end must be the same kind of piece as the start");
					return;
				}
				// The player picked both ends, so allow slope-sized height steps between neighbouring pieces: on a
				// hill each rigid piece sits one slope step above the last. "Nearest height first" in the chain
				// still keeps it on one course.
				float pieceLength = (segments[startIndex].A - segments[startIndex].B).magnitude;
				// Two pieces of the same kind join when their ends touch within the join tolerance, measured in
				// every direction. Height is not a separate rule here: a fence rising up a hill joins at the shared
				// joint, and stacked courses are a full piece apart, so they never fall inside the tolerance.
				float joinTolerance = Mathf.Max(TraceJoinTolerance, pieceLength * 0.6f);
				float rangeTolerance = joinTolerance;
				polyline = LineTrace.ChainBetween(segments, startIndex, endIndex, joinTolerance, rangeTolerance, TraceMaxPieces, out used);
				if (polyline.Count < 2)
				{
					List<Vector3> reachedLine = LineTrace.Chain(segments, startIndex, joinTolerance, rangeTolerance, TraceMaxPieces, out int reached);
					if (reachedLine.Count >= 2)
					{
						LogNearestToPoint(segments, reachedLine[0], "chain end A");
						LogNearestToPoint(segments, reachedLine[reachedLine.Count - 1], "chain end B");
					}
					LogTraceNeighbours(segments, startIndex, wall.transform);
					Vector3 a = segments[startIndex].A, b = segments[endIndex].A;
					SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace end not joined: " + wallPrefabName + " pieces in range=" + segments.Count
						+ " start=" + a.ToString("F1") + " end=" + b.ToString("F1")
						+ " horizontal=" + new Vector2(a.x - b.x, a.z - b.z).magnitude.ToString("0.0") + "m vertical=" + Mathf.Abs(a.y - b.y).ToString("0.0")
						+ "m; the chain from the start reached " + reached + " piece(s) (pieceLength=" + pieceLength.ToString("0.0")
						+ " heightTolerance=" + rangeTolerance.ToString("0.0") + ").");
					Notify("Trace end is not joined to the start (gap or different course)");
					return;
				}
			}
			else polyline = LineTrace.Chain(segments, startIndex, TraceJoinTolerance, heightTolerance, TraceMaxPieces, out used);
			if (polyline.Count < 2)
			{
				Notify("Trace wall: could not follow '" + wallPrefabName + "'.");
				return;
			}

			_traceBase.Clear();
			_traceBase.AddRange(polyline);
			_traceAnchor = null;
			_traceWallPrefab = wallPrefabName;
			Player player = Player.m_localPlayer;
			// The row goes on the face the crosshair is on, wherever the player stands. A hit on the
			// top or bottom of the wall has no useful horizontal normal, so fall back to the player.
			bool sideFace = Mathf.Abs(aimNormal.y) < TraceSideFaceMaxNormalY;
			_traceOnTop = aimNormal.y >= TraceSideFaceMaxNormalY;
			Vector3 sideProbe = sideFace ? aimPoint + aimNormal * 0.3f : (player ? player.transform.position : aimPoint);
			_traceSide = LineTrace.SideOf(_traceBase, sideProbe);

			float wallHalfThickness = HalfTransverse(wallBounds, wallAlongX) * Mathf.Abs(wall.transform.lossyScale.x);
			Bounds rowBounds = PrefabPlacementMetadata.GetLocalRendererBounds(rowPrefab);
			float rowHalfDepth = HalfTransverse(rowBounds, RepeatsAlongX(rowMetadata, rowBounds));
			_traceRowStep = Mathf.Max(0.1f, rowHalfDepth * 2f);
			string snapNote = "root";
			float courseBaseY = polyline[0].y;
			if (_traceOnTop)
			{
				// On top: the row runs down the wall's centre line with its bottom on the aimed top face.
				// Level mode places the root at (waypoint Y - centre offset Y), so solve for the waypoint.
				courseBaseY = aimPoint.y;
				snapNote = "top face";
				_traceRowY = courseBaseY - rowBounds.min.y + rowMetadata.CenterOffset.y;
				_traceOffset = 0f;
			}
			else
			{
				// Level mode places the piece root at (waypoint Y - centre offset Y); choose the waypoint
				// so the piece's top lands on the base of the traced course.
				// Always the lowest snap, so the course height does not change with where the wall is aimed
				// (the nearest snap flipped between a block's top and bottom).
				var snapPoints = new List<Transform>();
				wall.GetSnapPoints(snapPoints);
				float lowestSnapY = float.MaxValue;
				foreach (Transform snap in snapPoints)
				{
					if (snap.position.y >= lowestSnapY) continue;
					lowestSnapY = snap.position.y;
					courseBaseY = snap.position.y;
					snapNote = "snap '" + snap.name + "'";
				}
				// Bottom of the row sits on the wall's bottom snap, so the row stands level with the fence base.
				_traceRowY = courseBaseY - rowBounds.min.y + rowMetadata.CenterOffset.y;
				_traceOffset = wallHalfThickness + rowHalfDepth;
			}
			_traceFirstOffset = _traceOffset;
			_traceRowCount = 1;

			SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Traced " + used + " " + wallPrefabName + " piece(s) into "
				+ polyline.Count + " vertices; aimed " + Utils.GetPrefabName(wall.gameObject) + " at " + aimPoint.ToString("F1")
				+ " normal " + aimNormal.ToString("F2") + "; level from " + snapNote + " y=" + courseBaseY.ToString("0.##")
				+ "; side=" + _traceSide + " offset=" + _traceOffset.ToString("0.##")
				+ (_traceOnTop ? " (on top of the wall)" : " (wall half " + wallHalfThickness.ToString("0.##") + " + row half " + rowHalfDepth.ToString("0.##") + ")") + ".");
			_traceLastOk = true;
			ApplyTrace("Traced " + used + " " + wallPrefabName + " piece(s)");
		}

		/// <summary>The traced wall offset by <paramref name="offset"/> to this trace's side, at the row height.</summary>
		private List<Vector3> TracedRowPoints(float offset)
		{
			List<Vector3> offsetLine = LineTrace.Offset(_traceBase, _traceSide * offset);
			var points = new List<Vector3>(offsetLine.Count);
			foreach (Vector3 p in offsetLine) points.Add(new Vector3(p.x, _traceRowY, p.z));
			return points;
		}

		/// <summary>Offset of row <paramref name="row"/> (0 is the one beside the wall). The outermost row follows the wheel nudge.</summary>
		private float TracedRowOffset(int row)
		{
			return row == _traceRowCount - 1 ? _traceOffset : _traceFirstOffset + row * _traceRowStep;
		}

		/// <summary>Rebuilds the waypoints from the traced wall at the current offset and refreshes the preview.</summary>
		private void ApplyTrace(string headline)
		{
			if (!HasTraceBase) return;
			_points.Clear();
			_points.AddRange(TracedRowPoints(_traceOffset));
			_traceRows.Clear();
			_traceRows.Add(new List<Vector3>(_points));
			_traceAnchor = null;
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			_traceActive = true;
			UpdatePreview();
			if (PluginConfig.IsVerboseLogging())
			{
				Player player = Player.m_localPlayer;
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace detail: player=" + (player ? player.transform.position.ToString("F1") : "?")
					+ " side=" + _traceSide + " offset=" + _traceOffset.ToString("0.##") + " rowPrefab=" + _prefabId
					+ "\n  wall=" + FormatPoints(_traceBase)
					+ "\n  row=" + FormatPoints(_points)
					+ "\n  placements(" + _placements.Count + ")=" + FormatPlacements(_placements));
			}
			string offsetText = "offset " + _traceOffset.ToString("0.##") + " m";
			Notify((headline != null ? headline + ", " : "Trace ") + offsetText + " (Shift+wheel nudges, " + PlaceChordLabel + " places)");
		}

		/// <summary>The row piece must be sampled and have a Ready profile, as for a normal Line Tool path.</summary>
		private bool TryGetTracePiece(out GameObject prefab, out ResolvedPrefabPlacement metadata)
		{
			prefab = null;
			metadata = default;
			if (_shapeToolMode)
			{
				Notify("Trace wall works with the Line Tool, not the Shape Tool.");
				return false;
			}
			if (string.IsNullOrWhiteSpace(_prefabId) || !ZNetScene.instance)
			{
				Notify("Trace wall: first sample the piece to lay along the wall with " + PluginConfig.FormatKeyLabel(PluginConfig.PieceRepeaterSampleKey.Value) + ".");
				return false;
			}
			prefab = ZNetScene.instance.GetPrefab(_prefabId);
			if (!prefab)
			{
				Notify("Trace wall: the sampled piece is no longer available.");
				return false;
			}
			metadata = ResolvePlacementMetadata(prefab);
			if (!AnarchyModeActive && (PrefabPlacementMetadata.IsExcludedPiece(prefab) || !metadata.Enabled || metadata.Status != RepeatProfileStatus.Ready))
			{
				Notify(ToolDisplayName + ": enable this piece and give it a Ready profile in VanillaPieces.yml before tracing.");
				return false;
			}
			_mode = PieceRepeaterMode.Path;
			return true;
		}

		/// <summary>
		/// Lays the traced row as straight runs along each wall segment instead of fitting the
		/// Line Tool's curve solver to it. The solver smooths the corners and spaces tiles unevenly,
		/// which left gaps against the wall at bends and random gaps along straight stretches.
		/// </summary>
		/// <summary>
		/// The wall top under a traced prop or tile: the highest build piece in the column around the row's height.
		/// The search starts above the row so block tops slightly above it are still found, which the
		/// Line Tool's surface lookup rejects.
		/// </summary>
		private bool TryGetTraceTopHeight(Vector3 position, out float height)
		{
			height = 0f;
			Vector3 origin = new Vector3(position.x, _traceRowY + 3f, position.z);
			int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _surfaceRayHits, 6f, ~0, QueryTriggerInteraction.Ignore);
			float best = float.NegativeInfinity;
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
				if (!collider.GetComponentInParent<Piece>()) continue;
				float hitY = _surfaceRayHits[i].point.y;
				if (hitY > best) best = hitY;
			}
			if (float.IsNegativeInfinity(best)) return false;
			height = best;
			return true;
		}

		/// <summary>Props (torches, poles) along a traced row at the Line Tool's fixed prop spacing, so gaps stay even on curves.</summary>
		private void GenerateTracedPropLayout(IList<Vector3> points, float spacing, ResolvedPrefabPlacement metadata)
		{
			float groundOffset = -metadata.BottomOffset;
			bool level = IsSameHeightHeld();
			List<Placement> center = SamplePolyline(points, spacing, false);
			foreach (Placement proposed in center)
			{
				if (_placements.Count >= MaximumPlacements) break;
				Quaternion rotation = PluginConfig.PieceRepeaterPropFollowCurve.Value
					? proposed.Rotation * Quaternion.Euler(0f, metadata.TangentYawOffset, 0f)
					: proposed.Rotation;
				Vector3 position = proposed.Position - rotation * metadata.CenterOffset;
				if (level)
				{
					position.y = _traceRowY - metadata.CenterOffset.y;
				}
				else if (_traceOnTop)
				{
					position.y = TryGetTraceTopHeight(position, out float topHeight)
						? topHeight + groundOffset : _traceRowY - metadata.CenterOffset.y;
				}
				else if (ZoneSystem.instance && ZoneSystem.instance.GetGroundHeight(position, out float terrainHeight) && !float.IsNaN(terrainHeight))
				{
					position.y = terrainHeight + groundOffset;
				}
				_placements.Add(new Placement(position, rotation));
			}
		}

		private void GenerateTracedLayout(IList<Vector3> points, GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			int firstPlacement = _placements.Count;
			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			bool alongX = RepeatsAlongX(metadata, bounds);
			Vector3 joinDelta = metadata.JoinEnd - metadata.JoinStart;
			joinDelta.y = 0f;
			bool hasJoins = joinDelta.sqrMagnitude > 0.0001f;
			Vector3 localAxis = hasJoins ? joinDelta.normalized : (alongX ? Vector3.right : Vector3.forward);
			float length = Mathf.Max(MinimumSpacing, hasJoins ? joinDelta.magnitude : (alongX ? bounds.size.x : bounds.size.z));
			// A negative spacing offset overlaps tiles; never let it shrink the step below half a tile.
			float step = Mathf.Max(length * 0.5f, length + _gapMeters);

			float wheelYaw = _rotationSteps * RotationStepDegrees;
			// Level height keeps the row at the wall course's base. Otherwise each tile follows the terrain
			// through the normal height mode, as the Line Tool does for any path.
			bool level = IsSameHeightHeld();
			int terrainFallbacks = 0;
			float rootY = _traceRowY - metadata.CenterOffset.y;
			float groundOffset = -metadata.BottomOffset;
			float fixedHeight = _traceRowY;
			foreach (TraceTile tile in LineTrace.Tile(new List<Vector3>(points), step, TraceCornerDegrees))
			{
				if (_placements.Count >= MaximumPlacements) break;
				float yaw = Vector3.SignedAngle(localAxis, tile.Direction, Vector3.up);
				Quaternion rotation = Quaternion.Euler(0f, wheelYaw, 0f) * Quaternion.Euler(0f, yaw, 0f);
				Vector3 correction = rotation * metadata.PositionOffset;
				Vector3 root = tile.Center - rotation * metadata.CenterOffset + correction;
				if (level)
				{
					root.y = rootY + correction.y;
				}
				else if (_traceOnTop)
				{
					// Following on top: take the wall top under each tile, so the row rides the top of the blocks.
					root.y = (TryGetTraceTopHeight(root, out float topHeight) ? topHeight + groundOffset : rootY) + correction.y;
				}
				else
				{
					// Sample the terrain directly: the build-piece surface lookup can pick the wall top instead of the ground.
					if (ZoneSystem.instance && ZoneSystem.instance.GetGroundHeight(root, out float terrainHeight) && !float.IsNaN(terrainHeight))
						root.y = terrainHeight + groundOffset;
					else
					{
						root.y += groundOffset;
						terrainFallbacks++;
					}
					root.y += correction.y;
				}
				_placements.Add(new Placement(root, rotation));
			}

			if (!level && terrainFallbacks > 0 && PluginConfig.IsVerboseLogging()) SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Traced row: " + terrainFallbacks + " tile(s) had no terrain sample and kept the row height.");
			// The outline follows the placed pieces, so it sits on the ground in follow mode rather than at the wall-base height.
			for (int i = firstPlacement; i < _placements.Count; i++) _outline.Add(_placements[i].Position);
		}

		/// <summary>
		/// Diagnostics for "not joined": the start piece's two ends and the closest ends of other pieces,
		/// to show whether neighbours are missing, offset sideways, at another height, or just beyond the join tolerance.
		/// </summary>
		/// <summary>Diagnostics for a chain that stops: the closest piece ends to a point at the chain's end.</summary>
		private static void LogNearestToPoint(List<TraceSegment> segments, Vector3 point, string label)
		{
			var scored = new List<KeyValuePair<float, string>>();
			for (int i = 0; i < segments.Count; i++)
			{
				Vector3[] ends = { segments[i].A, segments[i].B };
				for (int e = 0; e < 2; e++)
				{
					float dx = ends[e].x - point.x, dz = ends[e].z - point.z, dy = ends[e].y - point.y;
					float d = Mathf.Sqrt(dx * dx + dz * dz);
					scored.Add(new KeyValuePair<float, string>(d, "#" + i + (e == 0 ? "a" : "b") + " " + d.ToString("0.00") + "m dy=" + dy.ToString("0.00")));
				}
			}
			scored.Sort((x, y) => x.Key.CompareTo(y.Key));
			var parts = new List<string>();
			for (int i = 0; i < scored.Count && i < 5; i++) parts.Add(scored[i].Value);
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace " + label + " at " + point.ToString("F2") + ": nearest ends " + string.Join(" | ", parts));
		}

		private static void LogTraceNeighbours(List<TraceSegment> segments, int startIndex, Transform start)
		{
			Vector3[] ends = { segments[startIndex].A, segments[startIndex].B };
			string[] endNames = { "A", "B" };
			var scored = new List<KeyValuePair<float, string>>();
			for (int i = 0; i < segments.Count; i++)
			{
				if (i == startIndex) continue;
				Vector3[] others = { segments[i].A, segments[i].B };
				string[] otherNames = { "a", "b" };
				float best = float.MaxValue;
				string note = "";
				for (int e = 0; e < 2; e++)
					for (int o = 0; o < 2; o++)
					{
						float dx = ends[e].x - others[o].x, dz = ends[e].z - others[o].z;
						float dxz = Mathf.Sqrt(dx * dx + dz * dz);
						if (dxz >= best) continue;
						best = dxz;
						note = endNames[e] + "-" + otherNames[o] + " dy=" + (ends[e].y - others[o].y).ToString("0.00")
							+ " otherLength=" + (segments[i].A - segments[i].B).magnitude.ToString("0.00");
					}
				scored.Add(new KeyValuePair<float, string>(best, "#" + i + " " + best.ToString("0.00") + "m " + note));
			}

			scored.Sort((x, y) => x.Key.CompareTo(y.Key));
			var lines = new List<string>();
			for (int i = 0; i < scored.Count && i < 6; i++) lines.Add(scored[i].Value);
			Vector3 axis = ends[1] - ends[0];
			SonicWorldToolsPlugin.ModLogger?.LogInfo("[Line Tool] Trace neighbours of start piece: A=" + ends[0].ToString("F2") + " B=" + ends[1].ToString("F2")
				+ " axisPitch=" + (Mathf.Atan2(axis.y, new Vector2(axis.x, axis.z).magnitude) * Mathf.Rad2Deg).ToString("0.0") + "deg yaw=" + start.eulerAngles.y.ToString("0")
				+ " pitch=" + start.eulerAngles.x.ToString("0") + " roll=" + start.eulerAngles.z.ToString("0")
				+ " joinTolerance=" + TraceJoinTolerance.ToString("0.00") + "m; closest: " + string.Join(" | ", lines));
		}

		private static string FormatPoints(IList<Vector3> points)
		{
			var parts = new List<string>(points.Count);
			foreach (Vector3 p in points) parts.Add("(" + p.x.ToString("0.0") + "," + p.y.ToString("0.0") + "," + p.z.ToString("0.0") + ")");
			return string.Join(" ", parts);
		}

		private static string FormatPlacements(IList<Placement> placements)
		{
			var parts = new List<string>(placements.Count);
			foreach (Placement p in placements)
				parts.Add("(" + p.Position.x.ToString("0.0") + "," + p.Position.y.ToString("0.0") + "," + p.Position.z.ToString("0.0")
					+ " yaw" + p.Rotation.eulerAngles.y.ToString("0") + ")");
			return string.Join(" ", parts);
		}

		private static bool RepeatsAlongX(ResolvedPrefabPlacement metadata, Bounds bounds)
		{
			if (metadata.Axis == PrefabRepeatAxis.X) return true;
			if (metadata.Axis == PrefabRepeatAxis.Z) return false;
			return bounds.size.x >= bounds.size.z;
		}

		private static float HalfTransverse(Bounds bounds, bool alongX) => (alongX ? bounds.size.z : bounds.size.x) * 0.5f;

		/// <summary>Both ends of a piece along its repeat axis, at the piece root height.</summary>
		/// <summary>
		/// Both ends of a piece along its repeat axis. Uses the profile's join points when it has them, the
		/// same points the Line Tool joins pieces by, so neighbours that share a joint actually match.
		/// Falls back to the rendered bounds when the profile has no joins.
		/// </summary>
		private static TraceSegment PieceEnds(Transform piece, Bounds localBounds, bool alongX, ResolvedPrefabPlacement metadata)
		{
			Vector3 scale = piece.lossyScale;
			if (metadata.HasJoins)
			{
				return new TraceSegment(
					piece.position + piece.rotation * Vector3.Scale(metadata.JoinStart, scale),
					piece.position + piece.rotation * Vector3.Scale(metadata.JoinEnd, scale));
			}
			Vector3 center = localBounds.center;
			Vector3 a = alongX ? new Vector3(localBounds.min.x, 0f, center.z) : new Vector3(center.x, 0f, localBounds.min.z);
			Vector3 b = alongX ? new Vector3(localBounds.max.x, 0f, center.z) : new Vector3(center.x, 0f, localBounds.max.z);
			return new TraceSegment(
				piece.position + piece.rotation * Vector3.Scale(a, scale),
				piece.position + piece.rotation * Vector3.Scale(b, scale));
		}
	}
}
