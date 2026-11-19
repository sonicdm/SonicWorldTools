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
	/// <summary>Line Tool path layout: waypoint polyline sampling, edge-chain joins, corner pivots and path diagnostics.</summary>
	internal sealed partial class PieceRepeaterController
	{
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
			if (pathPoints.Count == 0 || !ToolInput.WasModifierHeld(PluginConfig.PieceRepeaterJointModifier.Value)) return aim;
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
	}
}
