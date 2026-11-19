using System;
using System.Collections.Generic;
using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Settings a solver run needs from the caller: locked start, wheel yaw, gap and limits.</summary>
	internal struct EdgeChainRun
	{
		internal bool HasLockedStart;
		internal Placement LockedStart;
		internal Vector3 LockedStartAnchorLocal;
		internal bool LockedStartAnchorIsSnap;
		internal float WheelYaw;
		internal float Gap;
		internal float MinimumSpacing;
		internal int MaxPlacements;
	}

	/// <summary>
	/// Edge-to-edge chain solver shared by the Line Tool and the Shape Tool. Places rigid pieces along a
	/// sampled polyline so neighbouring pieces meet at their joins, turning corners at the joint.
	/// Free of controller state: callers pass the run settings, the output list and a height function.
	/// </summary>
	internal static class EdgeChainSolver
	{
		internal static void Solve(IList<Vector3> path, float spacing, GameObject prefab,
			ResolvedPrefabPlacement metadata, EdgeChainRun run, List<Placement> output,
			Func<Vector3, Vector3> placeHeight)
		{
			if (path == null || path.Count < 2) return;
			var distances = new List<float>(path.Count) { 0f };
			for (int i = 1; i < path.Count; i++)
			{
				Vector3 delta = path[i] - path[i - 1]; delta.y = 0f;
				distances.Add(distances[i - 1] + delta.magnitude);
			}
			float total = distances[distances.Count - 1];
			if (total < run.MinimumSpacing) return;

			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			Vector3 joinDelta = metadata.JoinEnd - metadata.JoinStart;
			joinDelta.y = 0f;
			float length = Mathf.Max(run.MinimumSpacing, joinDelta.magnitude);
			float width = Mathf.Max(0.01f, metadata.Axis == PrefabRepeatAxis.X
				? bounds.size.z : bounds.size.x);
			Vector3 localAxis = metadata.JoinEnd - metadata.JoinStart;
			localAxis.y = 0f;
			if (localAxis.sqrMagnitude < 0.0001f) return;
			// Use the same outgoing end chosen for the anchored first piece. The
			// sign is applied exactly once in both the starter and fitted run.
			if (run.HasLockedStart)
				localAxis *= PieceRepeaterPathMath.ResolveOutgoingAxisSign(
					run.LockedStartAnchorLocal, metadata.JoinStart, metadata.JoinEnd, run.LockedStartAnchorIsSnap);

			Placement previous = run.HasLockedStart
				? run.LockedStart
				: new Placement(path[0] - Quaternion.identity * metadata.JoinEnd, Quaternion.identity);
			Vector3 previousCenter = previous.Position + previous.Rotation * metadata.CenterOffset;
			// The first piece may already include the user's wheel offset when its
			// secant heading was resolved. Remove that offset for fit calculations;
			// manual rotation changes orientation in place, not the solved centers.
			Quaternion fitStartRotation = run.HasLockedStart
				? Quaternion.Euler(0f, -run.WheelYaw, 0f) * previous.Rotation
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
			while (output.Count < run.MaxPlacements && guard++ < run.MaxPlacements)
			{
				if (!PieceRepeaterPathMath.TryFindNextCenter(path, distances, cursor, total,
					previousCenter, previousForward, localAxis, length, width, run.Gap,
					run.MinimumSpacing, 0f, out float accepted, out Vector3 nextCenter, out Quaternion solvedRotation)) break;
				// Wheel rotation is an in-place orientation adjustment. Keep it out of
				// the spacing solver so rotating the pieces cannot change their centers
				// or make the batch gain/lose pieces.
				Quaternion placementRotation = Quaternion.Euler(0f, run.WheelYaw, 0f) * solvedRotation;
				Vector3 correction = placementRotation * metadata.PositionOffset;
				Vector3 root = nextCenter - placementRotation * metadata.CenterOffset + correction;
				if (output.Count > 0)
				{
					Placement last = output[output.Count - 1];
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
				root = placeHeight(root);
				root.y += correction.y;
				output.Add(new Placement(root, placementRotation));
				// Keep the solver on the sampled route. Per-piece root/height
				// corrections affect rendering, never the next center search.
				previousCenter = nextCenter;
				previousForward = solvedRotation * localAxis.normalized; previousForward.y = 0f; previousForward.Normalize();
				cursor = accepted;
			}
		}

		internal static Vector3 GetEdgeJoinPoint(Placement placement, GameObject prefab,
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

		internal static void GetCornerPivotSides(Quaternion previousRotation, Quaternion nextRotation,
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

		internal static bool ShouldPivotAtOutsideCorner(float turn, float outsideBreak, float insideBreak)
		{
			const float seamBreakTolerance = 0.02f;
			return Mathf.Abs(turn) >= 90f || Mathf.Max(outsideBreak, insideBreak) > seamBreakTolerance;
		}

		internal static float GetCenterPivotAnchorError(Placement previous, Placement next, GameObject prefab,
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
	}
}
