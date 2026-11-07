using System.Collections.Generic;
using System;
using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Pure path sampling and rigid-rectangle overlap math shared by the tool and tests.</summary>
	internal static class PieceRepeaterPathMath
	{
		internal static Vector3 BoundedJoinCorrection(Vector3 previousJoin, Vector3 nextJoin,
			float maximumRouteDeviation)
		{
			Vector3 correction = previousJoin - nextJoin;
			correction.y = 0f;
			return Vector3.ClampMagnitude(correction, Mathf.Max(0f, maximumRouteDeviation));
		}

		internal static float ResolveFixedRootHeight(float firstPointCenterY, float centerOffsetY,
			bool hasLockedStart, float lockedStartRootY)
		{
			return hasLockedStart ? lockedStartRootY : firstPointCenterY - centerOffsetY;
		}

		internal static float ResolveOutgoingAxisSign(Vector3 localAnchor, Vector3 joinStart,
			Vector3 joinEnd, bool anchorIsSnap)
		{
			if (!anchorIsSnap) return 1f;
			Vector3 axis = joinEnd - joinStart;
			axis.y = 0f;
			if (axis.sqrMagnitude < 0.0001f) return 1f;
			axis.Normalize();
			Vector3 midpoint = (joinStart + joinEnd) * 0.5f;
			float anchorAlongAxis = Vector3.Dot(localAnchor - midpoint, axis);
			return anchorAlongAxis > 0.02f ? -1f : 1f;
		}

		internal static bool TryFindNextCenter(IList<Vector3> path, IList<float> distances,
			float cursor, float total, Vector3 previousCenter, Vector3 previousForward, Vector3 localAxis,
			float length, float width, float extraGap, float minimumStep, float extraYaw,
			out float acceptedDistance, out Vector3 center, out Quaternion rotation)
		{
			acceptedDistance = 0f;
			center = Vector3.zero;
			rotation = Quaternion.identity;
			float low = Mathf.Max(0f, cursor + minimumStep);
			float searchEnd = Mathf.Min(total, cursor + length + width + Mathf.Max(0f, extraGap) + 0.05f);
			if (low >= searchEnd) return false;

			// The overlap predicate need not be monotonic across an S-curve: it can
			// fail at a bend and become valid again after the curve reverses. Scan
			// forward for the first invalid interval, then refine only that boundary
			// so the solver cannot jump across an uncovered gap to a later valid one.
			const int scanSteps = 32;
			float accepted = low;
			float previousDistance = low;
			Vector3 lowCenter = SamplePathDistance(path, distances, low);
			Vector3 lowDelta = lowCenter - previousCenter;
			lowDelta.y = 0f;
			if (lowDelta.magnitude < minimumStep)
			{
				// Arc distance can advance while a tight reversal barely moves in
				// world space. Do not stack pieces at nearly the same center.
				bool foundProgress = false;
				for (int scan = 1; scan <= scanSteps; scan++)
				{
					float candidate = Mathf.Lerp(low, searchEnd, (float)scan / scanSteps);
					Vector3 candidateDelta = SamplePathDistance(path, distances, candidate) - previousCenter;
					candidateDelta.y = 0f;
					if (candidateDelta.magnitude < minimumStep) continue;
					low = candidate;
					previousDistance = candidate;
					foundProgress = true;
					break;
				}
				if (!foundProgress) return false;
				accepted = low;
			}
			if (!IsOverlapAccepted(path, distances, low, previousCenter, previousForward,
				localAxis, length, width, extraGap, extraYaw, minimumStep))
			{
				// Extremely tight geometry can require deliberate body overlap even
				// at the first safe center step; preserve progress without stacking.
				accepted = low;
			}
			else
			{
				for (int scan = 1; scan <= scanSteps; scan++)
				{
					float candidate = Mathf.Lerp(low, searchEnd, (float)scan / scanSteps);
					if (!IsOverlapAccepted(path, distances, candidate, previousCenter, previousForward,
						localAxis, length, width, extraGap, extraYaw, minimumStep))
					{
						float rejected = candidate;
						float acceptedBefore = previousDistance;
						for (int iteration = 0; iteration < 24; iteration++)
						{
							float mid = (acceptedBefore + rejected) * 0.5f;
							if (IsOverlapAccepted(path, distances, mid, previousCenter, previousForward,
								localAxis, length, width, extraGap, extraYaw, minimumStep)) acceptedBefore = mid;
							else rejected = mid;
						}
						accepted = acceptedBefore;
						break;
					}
					accepted = candidate;
					previousDistance = candidate;
				}
			}

			if (accepted < cursor + minimumStep * 0.99f) return false;
			acceptedDistance = accepted;
			center = SamplePathDistance(path, distances, accepted);
			Vector3 tangent = TangentAtDistance(path, distances, accepted, Mathf.Clamp(length * 0.25f, 0.1f, 0.5f));
			if (tangent.sqrMagnitude < 0.0001f) return false;
			rotation = RotationFromTangent(tangent, localAxis, extraYaw);
			if (accepted >= total - 0.0001f)
			{
				// The last route point is not itself a required piece center. If it
				// falls before the next full repeat interval, placing a rigid piece
				// there creates the large terminal overlaps seen on every wall type.
				Vector3 candidateForward = rotation * localAxis.normalized;
				float unusedReach = ProjectionOverlapSlack(previousCenter, center,
					previousForward, candidateForward, length, width, extraGap);
				if (unusedReach > 0.05f) return false;
			}
			return true;
		}

		private static bool IsOverlapAccepted(IList<Vector3> path, IList<float> distances, float distance,
			Vector3 previousCenter, Vector3 previousForward, Vector3 localAxis, float length, float width,
			float extraGap, float extraYaw, float minimumCenterDistance)
		{
			Vector3 candidateCenter = SamplePathDistance(path, distances, distance);
			Vector3 delta = candidateCenter - previousCenter;
			delta.y = 0f;
			if (delta.sqrMagnitude < minimumCenterDistance * minimumCenterDistance) return false;
			Vector3 tangent = TangentAtDistance(path, distances, distance, Mathf.Clamp(length * 0.25f, 0.1f, 0.5f));
			if (tangent.sqrMagnitude < 0.0001f) return false;
			Quaternion candidateRotation = RotationFromTangent(tangent, localAxis, extraYaw);
			Vector3 candidateForward = candidateRotation * localAxis.normalized;
			return ProjectionOverlapSlack(previousCenter, candidateCenter, previousForward,
				candidateForward, length, width, extraGap) >= 0f;
		}

		private static Quaternion RotationFromTangent(Vector3 tangent, Vector3 localAxis, float extraYaw)
		{
			tangent.y = localAxis.y = 0f;
			if (tangent.sqrMagnitude < 0.0001f || localAxis.sqrMagnitude < 0.0001f) return Quaternion.identity;
			float halfYaw = ((float)Math.Atan2(tangent.x, tangent.z) - (float)Math.Atan2(localAxis.x, localAxis.z)
				+ extraYaw * (float)Math.PI / 180f) * 0.5f;
			return new Quaternion(0f, (float)Math.Sin(halfYaw), 0f, (float)Math.Cos(halfYaw));
		}

		internal static List<Vector3> SampleCatmullRom(IList<Vector3> points, float smoothingDistance, int samplesPerSegment = 30)
		{
			var sampled = new List<Vector3>();
			if (points == null || points.Count == 0) return sampled;
			var controls = new List<Vector3>(points.Count);
			for (int i = 0; i < points.Count; i++)
				if (controls.Count == 0 || (points[i] - controls[controls.Count - 1]).sqrMagnitude > 0.000001f)
					controls.Add(points[i]);
			if (controls.Count == 1) { sampled.Add(controls[0]); return sampled; }
			samplesPerSegment = Mathf.Max(1, samplesPerSegment);

			for (int segment = 0; segment < controls.Count - 1; segment++)
			{
				Vector3 p0 = controls[Mathf.Max(0, segment - 1)];
				Vector3 p1 = controls[segment];
				Vector3 p2 = controls[segment + 1];
				Vector3 p3 = controls[Mathf.Min(controls.Count - 1, segment + 2)];
				Vector3 m1 = Vector3.ClampMagnitude((p2 - p0) * 0.5f, smoothingDistance);
				Vector3 m2 = Vector3.ClampMagnitude((p3 - p1) * 0.5f, smoothingDistance);
				int first = segment == 0 ? 0 : 1;
				for (int i = first; i <= samplesPerSegment; i++)
				{
					float t = (float)i / samplesPerSegment;
					float t2 = t * t;
					float t3 = t2 * t;
					float h00 = 2f * t3 - 3f * t2 + 1f;
					float h10 = t3 - 2f * t2 + t;
					float h01 = -2f * t3 + 3f * t2;
					float h11 = t3 - t2;
					Vector3 value = h00 * p1 + h10 * m1 + h01 * p2 + h11 * m2;
					if (sampled.Count == 0 || (value - sampled[sampled.Count - 1]).sqrMagnitude > 0.000001f)
						sampled.Add(value);
				}
			}
			return sampled;
		}

		/// <summary>
		/// WEC-style centripetal Catmull-Rom. Keeps the existing sampler's sample count
		/// so A/B comparisons primarily measure the curve shape, not solver resolution.
		/// </summary>
		internal static List<Vector3> SampleCentripetalCatmullRom(IList<Vector3> points, int samplesPerSegment = 30)
		{
			var sampled = new List<Vector3>();
			if (points == null || points.Count == 0) return sampled;

			var controls = new List<Vector3>(points.Count);
			for (int i = 0; i < points.Count; i++)
				if (controls.Count == 0 || (points[i] - controls[controls.Count - 1]).sqrMagnitude > 0.000001f)
					controls.Add(points[i]);
			if (controls.Count == 1)
			{
				sampled.Add(controls[0]);
				return sampled;
			}

			samplesPerSegment = Mathf.Max(1, samplesPerSegment);
			for (int segment = 0; segment < controls.Count - 1; segment++)
			{
				Vector3 p1 = controls[segment];
				Vector3 p2 = controls[segment + 1];
				Vector3 p0 = segment > 0 ? controls[segment - 1] : 2f * p1 - p2;
				Vector3 p3 = segment < controls.Count - 2 ? controls[segment + 2] : 2f * p2 - p1;
				int first = segment == 0 ? 0 : 1;
				for (int i = first; i <= samplesPerSegment; i++)
				{
					Vector3 value = CentripetalCatmullRom(p0, p1, p2, p3, (float)i / samplesPerSegment);
					if (sampled.Count == 0 || (value - sampled[sampled.Count - 1]).sqrMagnitude > 0.000001f)
						sampled.Add(value);
				}
			}
			return sampled;
		}

		private static float CentripetalKnot(Vector3 a, Vector3 b)
		{
			return Mathf.Max(Mathf.Sqrt(Vector3.Distance(a, b)), 0.01f);
		}

		private static Vector3 CentripetalInterpolate(Vector3 a, Vector3 b, float ta, float tb, float t)
		{
			return Vector3.LerpUnclamped(a, b, (t - ta) / (tb - ta));
		}

		private static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
		{
			float t0 = 0f;
			float t1 = t0 + CentripetalKnot(p0, p1);
			float t2 = t1 + CentripetalKnot(p1, p2);
			float t3 = t2 + CentripetalKnot(p2, p3);
			float u = Mathf.Lerp(t1, t2, t);
			Vector3 a1 = CentripetalInterpolate(p0, p1, t0, t1, u);
			Vector3 a2 = CentripetalInterpolate(p1, p2, t1, t2, u);
			Vector3 a3 = CentripetalInterpolate(p2, p3, t2, t3, u);
			Vector3 b1 = CentripetalInterpolate(a1, a2, t0, t2, u);
			Vector3 b2 = CentripetalInterpolate(a2, a3, t1, t3, u);
			return CentripetalInterpolate(b1, b2, t1, t2, u);
		}

		internal static Vector3 SamplePathDistance(IList<Vector3> path, IList<float> distances, float distance)
		{
			if (distance <= 0f) return path[0];
			int last = distances.Count - 1;
			if (distance >= distances[last]) return path[path.Count - 1];

			// Solver queries are repeated many times per preview. Binary-search the
			// cumulative arc-length table instead of rescanning from its beginning.
			int low = 1;
			int high = last;
			while (low < high)
			{
				int mid = low + (high - low) / 2;
				if (distance > distances[mid]) low = mid + 1;
				else high = mid;
			}
			float span = distances[low] - distances[low - 1];
			return span > 0.0001f
				? Vector3.Lerp(path[low - 1], path[low], (distance - distances[low - 1]) / span)
				: path[low];
		}

		internal static Vector3 TangentAtDistance(IList<Vector3> path, IList<float> distances,
			float distance, float halfWindow)
		{
			if (path == null || distances == null || path.Count < 2 || distances.Count != path.Count)
				return Vector3.zero;
			float total = distances[distances.Count - 1];
			float before = Mathf.Max(0f, distance - Mathf.Max(0.001f, halfWindow));
			float after = Mathf.Min(total, distance + Mathf.Max(0.001f, halfWindow));
			Vector3 tangent = SamplePathDistance(path, distances, after) - SamplePathDistance(path, distances, before);
			tangent.y = 0f;
			return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.zero;
		}

		internal static float ProjectionOverlapSlack(Vector3 previousCenter, Vector3 nextCenter,
			Vector3 previousForward, Vector3 nextForward, float length, float width, float extraGap)
		{
			Vector3 delta = nextCenter - previousCenter;
			delta.y = 0f;
			float distance = delta.magnitude;
			if (distance < 0.0001f) return length + extraGap;
			Vector3 q = delta / distance;
			previousForward.y = nextForward.y = 0f;
			if (previousForward.sqrMagnitude > 0.0001f) previousForward.Normalize();
			if (nextForward.sqrMagnitude > 0.0001f) nextForward.Normalize();
			Vector3 previousSide = Vector3.Cross(Vector3.up, previousForward);
			Vector3 nextSide = Vector3.Cross(Vector3.up, nextForward);
			float reach = length * 0.5f * (Vector3.Dot(previousForward, q) + Vector3.Dot(nextForward, q))
				- width * 0.5f * Mathf.Abs(Vector3.Dot(previousSide, q) - Vector3.Dot(nextSide, q)) + extraGap;
			return reach - distance;
		}
	}
}
