using System;
using System.Collections.Generic;
using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>A placed piece reduced to the two ends of its repeat axis, in world space.</summary>
	internal readonly struct TraceSegment
	{
		internal readonly Vector3 A;
		internal readonly Vector3 B;

		internal TraceSegment(Vector3 a, Vector3 b)
		{
			A = a;
			B = b;
		}
	}

	/// <summary>One tile of a traced row: where its centre goes and which way its repeat axis points (horizontal, unit).</summary>
	internal readonly struct TraceTile
	{
		internal readonly Vector3 Center;
		internal readonly Vector3 Direction;

		internal TraceTile(Vector3 center, Vector3 direction)
		{
			Center = center;
			Direction = direction;
		}
	}

	/// <summary>
	/// Pure math for tracing an existing wall: chain pieces end to end into a polyline,
	/// pick the side the player stands on, and offset the polyline to that side.
	/// Free of Unity runtime calls so it is unit-testable.
	/// </summary>
	internal static class LineTrace
	{
		/// <summary>
		/// Starting at <paramref name="startIndex"/>, follows segments whose ends meet (within
		/// <paramref name="tolerance"/> horizontally and <paramref name="heightTolerance"/> vertically)
		/// in both directions. The height check keeps the trace on one course of a stacked wall,
		/// whose rows share the same ends seen from above. Each shared joint becomes one polyline
		/// vertex (the midpoint of the two touching ends). Stops at gaps, at forks (the closest
		/// candidate wins), or after <paramref name="maxSegments"/>.
		/// </summary>
		internal static List<Vector3> Chain(IReadOnlyList<TraceSegment> segments, int startIndex, float tolerance, float heightTolerance,
			int maxSegments, out int usedSegments)
		{
			return ChainOrdered(segments, startIndex, tolerance, heightTolerance, maxSegments, out usedSegments, out _);
		}

		/// <summary>
		/// Like <see cref="Chain"/> but also returns the segment index behind every polyline edge:
		/// edge j runs from vertex j to vertex j+1 and belongs to <c>order[j]</c>.
		/// </summary>
		internal static List<Vector3> ChainOrdered(IReadOnlyList<TraceSegment> segments, int startIndex, float tolerance, float heightTolerance,
			int maxSegments, out int usedSegments, out List<int> order)
		{
			var result = new List<Vector3>();
			order = new List<int>();
			usedSegments = 0;
			if (segments == null || startIndex < 0 || startIndex >= segments.Count) return result;

			var used = new bool[segments.Count];
			used[startIndex] = true;
			usedSegments = 1;
			var forward = new List<Vector3> { segments[startIndex].A, segments[startIndex].B };
			var backward = new List<Vector3>();
			var forwardOrder = new List<int>();
			var backwardOrder = new List<int>();

			Extend(segments, used, forward, forwardOrder, tolerance, heightTolerance, maxSegments, ref usedSegments);
			// Walk the other way from A by extending a reversed copy, then stitch.
			backward.Add(segments[startIndex].B);
			backward.Add(segments[startIndex].A);
			Extend(segments, used, backward, backwardOrder, tolerance, heightTolerance, maxSegments, ref usedSegments);

			for (int i = backward.Count - 1; i >= 2; i--) result.Add(backward[i]);
			// backward[1] is A and may have been averaged with the next piece's end; prefer it.
			result.Add(backward[1]);
			for (int i = 1; i < forward.Count; i++) result.Add(forward[i]);

			for (int i = backwardOrder.Count - 1; i >= 0; i--) order.Add(backwardOrder[i]);
			order.Add(startIndex);
			order.AddRange(forwardOrder);
			return result;
		}

		/// <summary>
		/// The stretch of the wall from the piece at <paramref name="startIndex"/> to the piece at
		/// <paramref name="endIndex"/>, both included, running start to end. Empty when the two are not
		/// joined by a chain of touching pieces (a gap, a different course, or too far).
		/// </summary>
		internal static List<Vector3> ChainBetween(IReadOnlyList<TraceSegment> segments, int startIndex, int endIndex, float tolerance,
			float heightTolerance, int maxSegments, out int usedSegments)
		{
			List<Vector3> full = ChainOrdered(segments, startIndex, tolerance, heightTolerance, maxSegments, out _, out List<int> order);
			usedSegments = 0;
			int from = order.IndexOf(startIndex);
			int to = order.IndexOf(endIndex);
			if (from < 0 || to < 0) return new List<Vector3>();
			int low = Math.Min(from, to);
			int high = Math.Max(from, to);
			List<Vector3> slice = full.GetRange(low, high - low + 2);
			if (to < from) slice.Reverse();
			usedSegments = high - low + 1;

			// On a closed loop the two picks have two ways round. The direct slice is only one of them, so also
			// try the way that wraps through the chain's ends and keep the shorter one.
			int last = full.Count - 1;
			if (IsClosedLoop(full, tolerance, heightTolerance))
			{
				// Other way round: from the start piece's far point back through the chain's first point, then down
				// to the end piece's near point. Both picked pieces are included, as in the direct slice.
				var wrap = new List<Vector3>();
				for (int i = low + 1; i >= 0; i--) wrap.Add(full[i]);
				for (int i = last - 1; i >= high; i--) wrap.Add(full[i]);
				if (to < from) wrap.Reverse();
				if (PathLength(wrap) < PathLength(slice))
				{
					slice = wrap;
					usedSegments = wrap.Count - 1;
				}
			}
			return slice;
		}

		private static bool IsClosedLoop(List<Vector3> full, float tolerance, float heightTolerance)
		{
			if (full.Count < 4) return false;
			Vector3 first = full[0], last = full[full.Count - 1];
			return DistanceSqXZ(first, last) <= tolerance * tolerance && Math.Abs(first.y - last.y) <= heightTolerance;
		}

		private static float PathLength(List<Vector3> path)
		{
			float length = 0f;
			for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
			return length;
		}

		private static void Extend(IReadOnlyList<TraceSegment> segments, bool[] used, List<Vector3> chain, List<int> order, float tolerance,
			float heightTolerance, int maxSegments, ref int usedSegments)
		{
			float toleranceSq = tolerance * tolerance;
			while (usedSegments < maxSegments)
			{
				Vector3 end = chain[chain.Count - 1];
				int best = -1;
				bool bestFlipped = false;
				float bestDistance = float.MaxValue;
				for (int i = 0; i < segments.Count; i++)
				{
					if (used[i]) continue;
					float da = DistanceSqXZ(end, segments[i].A);
					float db = DistanceSqXZ(end, segments[i].B);
					float dyA = Math.Abs(end.y - segments[i].A.y);
					float dyB = Math.Abs(end.y - segments[i].B.y);
					// Among pieces that meet at this joint, prefer the one at the nearest height: walls with
					// courses offset by half a piece have several pieces sharing the same ends in XZ.
					float costA = (float)Math.Sqrt(da) + 0.5f * dyA;
					float costB = (float)Math.Sqrt(db) + 0.5f * dyB;
					if (dyA <= heightTolerance && da <= toleranceSq && costA < bestDistance) { best = i; bestFlipped = false; bestDistance = costA; }
					if (dyB <= heightTolerance && db <= toleranceSq && costB < bestDistance) { best = i; bestFlipped = true; bestDistance = costB; }
				}
				if (best < 0) return;

				used[best] = true;
				Vector3 near = bestFlipped ? segments[best].B : segments[best].A;
				Vector3 far = bestFlipped ? segments[best].A : segments[best].B;
				// A piece whose far end lands back on the traced line overlaps one already used
				// (a duplicate or doubled piece); following it would make the trace U-turn.
				if (IsOnChain(chain, far, toleranceSq, heightTolerance))
				{
					// A piece that closes a loop back onto the chain's first point is part of the loop, not an overlap.
					bool closesLoop = chain.Count >= 3 && DistanceSqXZ(far, chain[0]) <= toleranceSq
					                  && Math.Abs(far.y - chain[0].y) <= heightTolerance;
					if (!closesLoop) continue;
					usedSegments++;
					order.Add(best);
					chain[chain.Count - 1] = (end + near) * 0.5f;
					chain.Add(far);
					return;
				}
				usedSegments++;
				order.Add(best);
				chain[chain.Count - 1] = (end + near) * 0.5f;
				chain.Add(far);
			}
		}

		/// <summary>
		/// Lays tiles straight along every segment of <paramref name="polyline"/>. Each segment gets
		/// ceil(length / step) evenly spaced tiles, so its ends are flush and no gap exceeds
		/// <paramref name="step"/>. At every interior vertex that turns by at least
		/// <paramref name="cornerDegrees"/> one extra tile sits on the vertex, pointing along the
		/// bisector, to cover the wedge the two neighbouring tiles leave open.
		/// </summary>
		internal static List<TraceTile> Tile(IReadOnlyList<Vector3> polyline, float step, float cornerDegrees)
		{
			var tiles = new List<TraceTile>();
			if (polyline == null || polyline.Count < 2 || step <= 1e-4f) return tiles;
			int segments = polyline.Count - 1;
			var directions = new Vector3[segments];
			var lengths = new float[segments];
			for (int i = 0; i < segments; i++)
			{
				Vector3 d = polyline[i + 1] - polyline[i];
				d.y = 0f;
				lengths[i] = d.magnitude;
				directions[i] = lengths[i] > 1e-4f ? d / lengths[i] : Vector3.zero;
			}

			for (int i = 0; i < segments; i++)
			{
				if (directions[i] == Vector3.zero) continue;
				int count = Math.Max(1, (int)Math.Ceiling(lengths[i] / step - 1e-3));
				for (int k = 0; k < count; k++)
					tiles.Add(new TraceTile(Vector3.Lerp(polyline[i], polyline[i + 1], (k + 0.5f) / count), directions[i]));

				if (i >= segments - 1 || directions[i + 1] == Vector3.zero) continue;
				if (Vector3.Angle(directions[i], directions[i + 1]) < cornerDegrees) continue;
				Vector3 bisector = directions[i] + directions[i + 1];
				if (bisector.sqrMagnitude > 1e-4f) tiles.Add(new TraceTile(polyline[i + 1], bisector.normalized));
			}

			return tiles;
		}

		private static bool IsOnChain(List<Vector3> chain, Vector3 point, float toleranceSq, float heightTolerance)
		{
			for (int i = 0; i < chain.Count; i++)
				if (DistanceSqXZ(chain[i], point) <= toleranceSq && Math.Abs(chain[i].y - point.y) <= heightTolerance) return true;
			return false;
		}

		/// <summary>+1 when <paramref name="point"/> is on the left of the polyline (normal (-dz, 0, dx)), -1 on the right.</summary>
		internal static int SideOf(IReadOnlyList<Vector3> polyline, Vector3 point)
		{
			if (polyline == null || polyline.Count < 2) return 1;
			float bestSq = float.MaxValue;
			float side = 1f;
			for (int i = 0; i < polyline.Count - 1; i++)
			{
				Vector3 a = polyline[i];
				Vector3 b = polyline[i + 1];
				float dx = b.x - a.x, dz = b.z - a.z;
				float lengthSq = dx * dx + dz * dz;
				if (lengthSq < 1e-8f) continue;
				float t = Mathf.Clamp01(((point.x - a.x) * dx + (point.z - a.z) * dz) / lengthSq);
				float cx = a.x + dx * t, cz = a.z + dz * t;
				float distSq = (point.x - cx) * (point.x - cx) + (point.z - cz) * (point.z - cz);
				if (distSq < bestSq)
				{
					bestSq = distSq;
					side = (point.x - cx) * -dz + (point.z - cz) * dx;
				}
			}
			return side >= 0f ? 1 : -1;
		}

		/// <summary>
		/// Offsets every vertex sideways by <paramref name="distance"/> (positive = left, normal
		/// (-dz, 0, dx)). Interior vertices use a mitred offset so straight runs stay exactly
		/// <paramref name="distance"/> from the source; the mitre is capped at 3x on very sharp turns.
		/// Y is kept from the source vertex.
		/// </summary>
		internal static List<Vector3> Offset(IReadOnlyList<Vector3> polyline, float distance)
		{
			var result = new List<Vector3>();
			if (polyline == null) return result;
			int count = polyline.Count;
			for (int i = 0; i < count; i++)
			{
				Vector2 normalIn = i > 0 ? LeftNormal(polyline[i - 1], polyline[i]) : Vector2.zero;
				Vector2 normalOut = i < count - 1 ? LeftNormal(polyline[i], polyline[i + 1]) : Vector2.zero;
				Vector2 offset;
				if (normalIn == Vector2.zero) offset = normalOut * distance;
				else if (normalOut == Vector2.zero) offset = normalIn * distance;
				else
				{
					Vector2 sum = normalIn + normalOut;
					float sumLength = sum.magnitude;
					if (sumLength < 1e-4f) offset = normalIn * distance; // a full U-turn; fall back to the incoming side
					else
					{
						Vector2 miter = sum / sumLength;
						float cos = Vector2.Dot(miter, normalIn);
						float scale = cos > 1f / 3f ? 1f / cos : 3f;
						offset = miter * (distance * scale);
					}
				}
				Vector3 p = polyline[i];
				result.Add(new Vector3(p.x + offset.x, p.y, p.z + offset.y));
			}
			return result;
		}

		private static Vector2 LeftNormal(Vector3 a, Vector3 b)
		{
			float dx = b.x - a.x, dz = b.z - a.z;
			float length = (float)Math.Sqrt(dx * dx + dz * dz);
			if (length < 1e-6f) return Vector2.zero;
			return new Vector2(-dz / length, dx / length);
		}

		private static float DistanceSqXZ(Vector3 a, Vector3 b)
		{
			float dx = a.x - b.x, dz = a.z - b.z;
			return dx * dx + dz * dz;
		}
	}
}
