using SonicWorldTools.Modules.PieceRepeater;
using UnityEngine;
using Xunit;

namespace SonicWorldTools.Tests
{
	public sealed class PieceRepeaterPathMathTests
	{
		[Fact]
		public void Join_correction_is_horizontal_and_cannot_escape_route_bound()
		{
			Vector3 correction = PieceRepeaterPathMath.BoundedJoinCorrection(
				new Vector3(3f, 7f, -2f), new Vector3(1f, -4f, 1f), 0.6f);
			Assert.InRange(correction.magnitude, 0.599f, 0.601f);
			Assert.Equal(0f, correction.y);
			Assert.True(Vector3.Dot(correction, new Vector3(2f, 0f, -3f)) > 0f);
		}

		[Fact]
		public void Level_mode_uses_the_anchored_root_height()
		{
			Assert.Equal(4.25f, PieceRepeaterPathMath.ResolveFixedRootHeight(
				4.85f, 0.6f, true, 4.25f));
		}

		[Fact]
		public void Level_mode_converts_an_unanchored_center_height_to_root_height()
		{
			Assert.Equal(4.25f, PieceRepeaterPathMath.ResolveFixedRootHeight(
				4.85f, 0.6f, false, 999f));
		}

		[Theory]
		[InlineData(-1f, 1f)]
		[InlineData(1f, -1f)]
		public void Snapped_start_chooses_the_repeat_end_away_from_the_existing_anchor(
			float anchorX, float expectedAxisSign)
		{
			float sign = PieceRepeaterPathMath.ResolveOutgoingAxisSign(
				new Vector3(anchorX, 0f, 0f), Vector3.left, Vector3.right, true);

			Assert.Equal(expectedAxisSign, sign);
		}

		[Fact]
		public void Unsnapped_start_keeps_the_default_repeat_axis()
		{
			Assert.Equal(1f, PieceRepeaterPathMath.ResolveOutgoingAxisSign(
				Vector3.right, Vector3.left, Vector3.right, false));
		}

		[Fact]
		public void CatmullRom_passes_through_waypoints_in_input_order()
		{
			var points = new[]
			{
				new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f),
				new Vector3(9f, 0f, 4f), new Vector3(14f, 0f, 4f)
			};
			var curve = PieceRepeaterPathMath.SampleCatmullRom(points, 1.5f);

			Assert.Equal((points.Length - 1) * 30 + 1, curve.Count);
			for (int i = 0; i < points.Length; i++)
			{
				Assert.Equal(points[i], curve[i * 30]);
			}
			Assert.All(curve, point =>
			{
				Assert.True(float.IsFinite(point.x));
				Assert.True(float.IsFinite(point.y));
				Assert.True(float.IsFinite(point.z));
			});
		}

		[Fact]
		public void Adding_a_waypoint_refits_the_existing_curve_instead_of_only_appending_a_leg()
		{
			var firstPair = new[] { Vector3.zero, Vector3.forward * 4f };
			var extended = new[]
			{
				Vector3.zero, Vector3.forward * 4f, Vector3.forward * 4f + Vector3.right * 4f
			};
			var oldCurve = PieceRepeaterPathMath.SampleCatmullRom(firstPair, 1.5f);
			var updatedCurve = PieceRepeaterPathMath.SampleCatmullRom(extended, 1.5f);

			Assert.True(Vector3.Distance(oldCurve[15], updatedCurve[15]) > 0.05f,
				"The new waypoint should change the fitted preview before the newest leg.");
			Assert.Equal(extended[1], updatedCurve[30]);
		}

		[Fact]
		public void CatmullRom_ignores_duplicate_consecutive_endpoints()
		{
			var points = new[]
			{
				Vector3.zero, Vector3.right * 4f, Vector3.right * 4f,
				new Vector3(7f, 0f, 3f)
			};
			var deduplicated = PieceRepeaterPathMath.SampleCatmullRom(
				new[] { points[0], points[1], points[3] }, 1.5f);
			var withDuplicate = PieceRepeaterPathMath.SampleCatmullRom(points, 1.5f);

			Assert.Equal(deduplicated.Count, withDuplicate.Count);
			for (int i = 0; i < deduplicated.Count; i++)
				Assert.InRange(Vector3.Distance(deduplicated[i], withDuplicate[i]), 0f, 0.0001f);
		}

		[Fact]
		public void Projection_solver_keeps_full_length_spacing_on_straight_runs()
		{
			Vector3 forward = Vector3.forward;
			float slack = PieceRepeaterPathMath.ProjectionOverlapSlack(
				Vector3.zero, Vector3.forward * 2f, forward, forward, 2f, 0.5f, 0f);

			Assert.InRange(slack, -0.0001f, 0.0001f);
		}

		[Fact]
		public void Fitted_solver_orients_reversed_runs_along_the_reverse_secant()
		{
			var path = new[] { Vector3.zero, Vector3.left * 4f };
			var distances = new System.Collections.Generic.List<float> { 0f, 4f };
			bool found = PieceRepeaterPathMath.TryFindNextCenter(path, distances, 0f, 4f,
				Vector3.zero, Vector3.left, Vector3.forward, 2f, 0.5f, 0f, 0.25f, 0f,
				out float accepted, out Vector3 center, out Quaternion rotation);

			Assert.True(found);
			Assert.InRange(accepted, 1.999f, 2.001f);
			Assert.InRange(center.x, -2.001f, -1.999f);
			Vector3 pieceForward = rotation * Vector3.forward;
			Assert.True(Vector3.Dot(pieceForward, Vector3.left) > 0.999f);
		}

		[Fact]
		public void Fitted_solver_does_not_force_a_piece_into_a_subminimum_leg()
		{
			var path = new[] { Vector3.zero, Vector3.right * 0.2f };
			var distances = new System.Collections.Generic.List<float> { 0f, 0.2f };
			bool found = PieceRepeaterPathMath.TryFindNextCenter(path, distances, 0f, 0.2f,
				Vector3.zero, Vector3.right, Vector3.forward, 2f, 0.5f, 0f, 0.25f, 0f,
				out _, out _, out _);

			Assert.False(found);
		}

		[Fact]
		public void Fitted_solver_does_not_finish_a_loop_with_a_near_duplicate_center()
		{
			const int samples = 64;
			const float radius = 0.2f;
			var path = new System.Collections.Generic.List<Vector3>(samples + 1);
			var distances = new System.Collections.Generic.List<float>(samples + 1) { 0f };
			for (int i = 0; i <= samples; i++)
			{
				float angle = i * (2f * Mathf.PI / samples);
				path.Add(new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
				if (i > 0) distances.Add(distances[i - 1] + Vector3.Distance(path[i - 1], path[i]));
			}

			bool found = PieceRepeaterPathMath.TryFindNextCenter(path, distances, 0f,
				distances[distances.Count - 1], path[0], Vector3.right, Vector3.right,
				2f, 0.5f, 0f, 0.25f, 0f, out _, out Vector3 center, out _);

			Assert.True(found);
			Assert.True(Vector3.Distance(center, path[0]) >= 0.25f - 0.001f,
				"A curved route must not emit an almost coincident final placement.");
		}

		[Fact]
		public void Arc_distance_sampling_binary_searches_uneven_segments_and_clamps_endpoints()
		{
			var points = new[] { Vector3.zero, Vector3.right * 2f, Vector3.right * 5f };
			var distances = new[] { 0f, 2f, 5f };

			Assert.Equal(Vector3.zero, PieceRepeaterPathMath.SamplePathDistance(points, distances, -1f));
			Assert.Equal(Vector3.right, PieceRepeaterPathMath.SamplePathDistance(points, distances, 1f));
			Assert.Equal(Vector3.right * 3.5f, PieceRepeaterPathMath.SamplePathDistance(points, distances, 3.5f));
			Assert.Equal(points[2], PieceRepeaterPathMath.SamplePathDistance(points, distances, 6f));
		}

		[Fact]
		public void Fitted_solver_does_not_add_a_full_wall_for_a_short_terminal_remainder()
		{
			var path = new[] { Vector3.zero, Vector3.forward * 4.4f };
			var distances = new[] { 0f, 4.4f };
			bool found = PieceRepeaterPathMath.TryFindNextCenter(path, distances, 4f, 4.4f,
				Vector3.forward * 4f, Vector3.forward, Vector3.forward,
				2f, 0.5f, 0f, 0.1f, 0f, out _, out _, out _);
			Assert.False(found);
		}

		[Theory]
		[InlineData(0f, 2f)]
		[InlineData(0.5f, 2.5f)]
		public void Fitted_centers_follow_piece_length_and_additive_gap_on_a_straight(float gap, float expectedStep)
		{
			var path = new[] { Vector3.zero, Vector3.right * 12f };
			var distances = new System.Collections.Generic.List<float> { 0f, 12f };
			bool found = PieceRepeaterPathMath.TryFindNextCenter(path, distances, 0f, 12f,
				Vector3.zero, Vector3.right, Vector3.forward, 2f, 0.5f, gap, 0.25f, 0f,
				out float acceptedDistance, out Vector3 center, out _);

			Assert.True(found);
			Assert.InRange(acceptedDistance, expectedStep - 0.001f, expectedStep + 0.001f);
			Assert.InRange(center.x, expectedStep - 0.001f, expectedStep + 0.001f);
			Assert.InRange(Mathf.Abs(center.z), 0f, 0.001f);
		}

		[Fact]
		public void Projection_solver_reduces_usable_spacing_at_a_turn()
		{
			Vector3 previousForward = Vector3.forward;
			Vector3 nextForward = Vector3.right;
			Vector3 q = (Vector3.forward + Vector3.right).normalized;
			Vector3 withinTurnReach = q * 1.05f;
			Vector3 beyondTurnReach = q * 1.2f;

			float acceptedSlack = PieceRepeaterPathMath.ProjectionOverlapSlack(
				Vector3.zero, withinTurnReach, previousForward, nextForward, 2f, 0.5f, 0f);
			float rejectedSlack = PieceRepeaterPathMath.ProjectionOverlapSlack(
				Vector3.zero, beyondTurnReach, previousForward, nextForward, 2f, 0.5f, 0f);

			Assert.True(acceptedSlack >= 0f);
			Assert.True(rejectedSlack < 0f);
		}

		[Fact]
		public void Tangent_uses_samples_on_both_sides_of_the_curve_point()
		{
			var path = new[]
			{
				Vector3.zero,
				Vector3.forward * 2f,
				Vector3.forward * 2f + Vector3.right * 2f
			};
			var distances = new[] { 0f, 2f, 4f };

			Vector3 tangent = PieceRepeaterPathMath.TangentAtDistance(path, distances, 2f, 0.25f);

			Assert.InRange(Vector3.Dot(tangent, (Vector3.forward + Vector3.right).normalized), 0.999f, 1.001f);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void Fitted_step_solver_returns_ordered_rigid_placements(bool sCurve)
		{
			Vector3[] controls = sCurve
				? new[] { Vector3.zero, new Vector3(5f, 0f, 0f), new Vector3(9f, 0f, 4f), new Vector3(14f, 0f, 4f), new Vector3(18f, 0f, 0f) }
				: new[] { Vector3.zero, new Vector3(12f, 0f, 0f) };
			var path = PieceRepeaterPathMath.SampleCatmullRom(controls, 1.5f);
			var distances = new System.Collections.Generic.List<float> { 0f };
			for (int i = 1; i < path.Count; i++)
			{
				Vector3 delta = path[i] - path[i - 1]; delta.y = 0f;
				distances.Add(distances[i - 1] + delta.magnitude);
			}

			float total = distances[distances.Count - 1];
			Vector3 localAxis = Vector3.forward;
			Vector3 previousForward = Vector3.right;
			Vector3 previousCenter = path[0];
			float cursor = 0f;
			int placements = 1;
			int guard = 0;
			bool tighterThanStraightSpacing = false;
			while (guard++ < 100 && PieceRepeaterPathMath.TryFindNextCenter(path, distances,
				cursor, total, previousCenter, previousForward, localAxis, 2f, 0.5f, 0f, 0.25f, 0f,
				out float nextDistance, out Vector3 nextCenter, out Quaternion nextRotation))
			{
				Assert.True(nextDistance >= cursor + 0.25f * 0.99f);
				Assert.True(float.IsFinite(nextCenter.x) && float.IsFinite(nextCenter.z));
				float centerStep = Vector3.Distance(previousCenter, nextCenter);
				Assert.InRange(centerStep, 0.249f, 2.001f);
			Vector3 pieceForward = nextRotation * localAxis;
			pieceForward.y = 0f;
			pieceForward.Normalize();
				Vector3 curveTangent = PieceRepeaterPathMath.TangentAtDistance(path, distances, nextDistance,
					Mathf.Clamp(2f * 0.25f, 0.1f, 0.5f));
				Assert.True(Vector3.Dot(curveTangent, pieceForward) > 0.999f,
					"Rigid piece heading must follow the local curve tangent.");
				float overlapSlack = PieceRepeaterPathMath.ProjectionOverlapSlack(previousCenter,
					nextCenter, previousForward, pieceForward, 2f, 0.5f, 0f);
				Assert.True(overlapSlack >= -0.01f,
					$"Fitted neighbors must meet the overlap condition; slack was {overlapSlack:0.000}.");
				if (centerStep < 1.9f) tighterThanStraightSpacing = true;
				previousCenter = nextCenter;
				previousForward = nextRotation * localAxis;
				cursor = nextDistance;
				placements++;
			}

			Assert.True(guard < 101, "Solver must stop at route end or placement limit.");
			Assert.True(placements >= 5);
			if (sCurve) Assert.True(tighterThanStraightSpacing, "The S-curve should tighten at least one interval to follow its bend.");
		}
	}
}
