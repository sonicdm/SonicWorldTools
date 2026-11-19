using System.Collections.Generic;
using SonicWorldTools.Modules.PieceRepeater;
using UnityEngine;
using Xunit;

namespace SonicWorldTools.Tests
{
	public sealed class LineTraceTests
	{
		private static TraceSegment Seg(float ax, float az, float bx, float bz) =>
			new TraceSegment(new Vector3(ax, 0f, az), new Vector3(bx, 0f, bz));

		[Fact]
		public void Chains_shuffled_and_flipped_pieces_in_order_from_the_middle()
		{
			// A bent wall of four 2 m pieces: (0,0)-(2,0)-(4,0)-(5.4,1.4)-(6.8,2.8), given out of order and with mixed directions.
			var segments = new List<TraceSegment>
			{
				Seg(4f, 0f, 2f, 0f),          // 0, flipped
				Seg(5.4f, 1.4f, 6.8f, 2.8f),  // 1
				Seg(0f, 0f, 2f, 0f),          // 2
				Seg(4f, 0f, 5.4f, 1.4f),      // 3
			};
			List<Vector3> line = LineTrace.Chain(segments, 3, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(4, used);
			Assert.Equal(5, line.Count);
			float[] xs = { 0f, 2f, 4f, 5.4f, 6.8f };
			bool ascending = line[0].x < line[4].x;
			for (int i = 0; i < 5; i++)
				Assert.Equal(xs[ascending ? i : 4 - i], line[i].x, 3);
		}

		[Fact]
		public void Joints_within_tolerance_are_merged_to_their_midpoint()
		{
			var segments = new List<TraceSegment> { Seg(0f, 0f, 2f, 0f), Seg(2.2f, 0f, 4f, 0f) };
			List<Vector3> line = LineTrace.Chain(segments, 0, 0.3f, 0.5f, 100, out _);
			Assert.Equal(3, line.Count);
			Assert.Equal(2.1f, line[1].x, 3);
		}

		[Fact]
		public void Gap_larger_than_tolerance_stops_the_trace()
		{
			var segments = new List<TraceSegment> { Seg(0f, 0f, 2f, 0f), Seg(3f, 0f, 5f, 0f) };
			List<Vector3> line = LineTrace.Chain(segments, 0, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(1, used);
			Assert.Equal(2, line.Count);
		}

		[Fact]
		public void Max_segments_caps_the_trace()
		{
			var segments = new List<TraceSegment>();
			for (int i = 0; i < 10; i++) segments.Add(Seg(i * 2f, 0f, i * 2f + 2f, 0f));
			LineTrace.Chain(segments, 0, 0.3f, 0.5f, 4, out int used);
			Assert.Equal(4, used);
		}

		[Fact]
		public void Stacked_courses_are_not_chained_together()
		{
			// Two rows of the same 2-piece wall, the second 2 m higher with identical XZ ends.
			var segments = new List<TraceSegment>
			{
				new TraceSegment(new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f)),
				new TraceSegment(new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f)),
				new TraceSegment(new Vector3(0f, 2f, 0f), new Vector3(2f, 2f, 0f)),
				new TraceSegment(new Vector3(2f, 2f, 0f), new Vector3(4f, 2f, 0f)),
			};
			List<Vector3> line = LineTrace.Chain(segments, 0, 0.3f, 0.9f, 100, out int used);
			Assert.Equal(2, used);
			Assert.All(line, p => Assert.Equal(0f, p.y));
		}

		[Fact]
		public void Half_piece_offset_courses_chain_but_prefer_the_nearest_height()
		{
			// A 2 m piece at y=1.0 sits between courses at y=0 and y=2 whose ends share its XZ position.
			var segments = new List<TraceSegment>
			{
				new TraceSegment(new Vector3(0f, 1f, 0f), new Vector3(2f, 1f, 0f)),   // 0: start, offset course
				new TraceSegment(new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f)),   // 1: 1 m below
				new TraceSegment(new Vector3(2f, 2f, 0f), new Vector3(4f, 2f, 0f)),   // 2: 1 m above
				new TraceSegment(new Vector3(2f, 1f, 0f), new Vector3(4f, 1f, 0f)),   // 3: same height
			};
			List<Vector3> line = LineTrace.Chain(segments, 0, 0.3f, 1.5f, 100, out int used);
			Assert.Equal(2, used);
			Assert.Equal(1f, line[line.Count - 1].y);
		}

		[Fact]
		public void Offset_course_pieces_one_metre_apart_still_form_one_wall()
		{
			var segments = new List<TraceSegment>
			{
				new TraceSegment(new Vector3(0f, 1f, 0f), new Vector3(2f, 1f, 0f)),
				new TraceSegment(new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f)),
			};
			LineTrace.Chain(segments, 0, 0.3f, 1.5f, 100, out int used);
			Assert.Equal(2, used);
		}

		[Fact]
		public void Small_height_steps_along_a_slope_still_chain()
		{
			var segments = new List<TraceSegment>
			{
				new TraceSegment(new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f)),
				new TraceSegment(new Vector3(2f, 0.4f, 0f), new Vector3(4f, 0.4f, 0f)),
			};
			LineTrace.Chain(segments, 0, 0.3f, 0.9f, 100, out int used);
			Assert.Equal(2, used);
		}

		[Fact]
		public void Overlapping_duplicate_piece_does_not_make_the_trace_turn_back()
		{
			// Pieces 0-2 form a straight wall; piece 3 duplicates piece 2 but reversed, which used to send the trace back.
			var segments = new List<TraceSegment>
			{
				Seg(0f, 0f, 2f, 0f),
				Seg(2f, 0f, 4f, 0f),
				Seg(4f, 0f, 6f, 0f),
				Seg(6f, 0f, 4f, 0f),
			};
			List<Vector3> line = LineTrace.Chain(segments, 0, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(3, used);
			Assert.Equal(4, line.Count);
			for (int i = 1; i < line.Count; i++) Assert.True(line[i].x > line[i - 1].x || line[0].x > line[1].x);
		}

		private static List<TraceSegment> StraightWall(int pieces)
		{
			var list = new List<TraceSegment>();
			for (int i = 0; i < pieces; i++) list.Add(Seg(i * 2f, 0f, i * 2f + 2f, 0f));
			return list;
		}

		[Fact]
		public void Between_returns_only_the_stretch_from_start_to_end_inclusive()
		{
			List<TraceSegment> wall = StraightWall(8);
			List<Vector3> line = LineTrace.ChainBetween(wall, 2, 5, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(4, used);
			Assert.Equal(5, line.Count);
			Assert.Equal(4f, line[0].x, 3);
			Assert.Equal(12f, line[4].x, 3);
		}

		[Fact]
		public void Between_runs_start_to_end_even_when_the_end_is_behind_the_start()
		{
			List<TraceSegment> wall = StraightWall(8);
			List<Vector3> line = LineTrace.ChainBetween(wall, 5, 2, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(4, used);
			Assert.Equal(12f, line[0].x, 3);
			Assert.Equal(4f, line[line.Count - 1].x, 3);
		}

		[Fact]
		public void Between_the_same_piece_is_a_single_piece()
		{
			List<Vector3> line = LineTrace.ChainBetween(StraightWall(5), 3, 3, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(1, used);
			Assert.Equal(2, line.Count);
		}

		[Fact]
		public void Between_is_empty_when_a_gap_separates_start_and_end()
		{
			var wall = new List<TraceSegment> { Seg(0f, 0f, 2f, 0f), Seg(2f, 0f, 4f, 0f), Seg(6f, 0f, 8f, 0f), Seg(8f, 0f, 10f, 0f) };
			List<Vector3> line = LineTrace.ChainBetween(wall, 0, 3, 0.3f, 0.5f, 100, out int used);
			Assert.Empty(line);
			Assert.Equal(0, used);
		}

		[Fact]
		public void Between_does_not_reach_a_piece_beyond_the_chain_cap()
		{
			Assert.Empty(LineTrace.ChainBetween(StraightWall(10), 0, 9, 0.3f, 0.5f, 4, out _));
		}

		[Fact]
		public void Between_follows_a_bend()
		{
			var wall = new List<TraceSegment>
			{
				Seg(0f, 0f, 2f, 0f), Seg(2f, 0f, 4f, 0f), Seg(4f, 0f, 4f, 2f), Seg(4f, 2f, 4f, 4f),
			};
			List<Vector3> line = LineTrace.ChainBetween(wall, 1, 3, 0.3f, 0.5f, 100, out int used);
			Assert.Equal(3, used);
			Assert.Equal(new Vector3(2f, 0f, 0f), line[0]);
			Assert.Equal(new Vector3(4f, 0f, 4f), line[3]);
		}

		[Fact]
		public void Fence_line_up_a_hill_chains_with_a_slope_sized_tolerance_only()
		{
			// 2 m pieces rising 1.1 m per piece, as a steep hill gives rigid pieces rooted at terrain height.
			var hill = new List<TraceSegment>();
			for (int i = 0; i < 6; i++)
				hill.Add(new TraceSegment(new Vector3(i * 2f, i * 1.1f, 0f), new Vector3(i * 2f + 2f, i * 1.1f, 0f)));
			Assert.Equal(1, LineTrace.ChainBetween(hill, 0, 0, 0.3f, 0.75f, 100, out _).Count - 1);
			Assert.Empty(LineTrace.ChainBetween(hill, 0, 5, 0.3f, 0.75f, 100, out _));
			List<Vector3> line = LineTrace.ChainBetween(hill, 0, 5, 0.3f, 2.4f, 100, out int used);
			Assert.Equal(6, used);
			Assert.Equal(7, line.Count);
		}

		[Fact]
		public void Side_is_left_or_right_of_travel_direction()
		{
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f) };
			// Left normal of +X travel is (-dz, dx) = (0, +1) → +Z.
			Assert.Equal(1, LineTrace.SideOf(line, new Vector3(5f, 0f, 3f)));
			Assert.Equal(-1, LineTrace.SideOf(line, new Vector3(5f, 0f, -3f)));
		}

		[Fact]
		public void Offset_keeps_straight_runs_at_exact_distance_and_y()
		{
			var line = new List<Vector3> { new Vector3(0f, 7f, 0f), new Vector3(10f, 7f, 0f) };
			List<Vector3> offset = LineTrace.Offset(line, 1.5f);
			Assert.Equal(new Vector3(0f, 7f, 1.5f), offset[0]);
			Assert.Equal(new Vector3(10f, 7f, 1.5f), offset[1]);
		}

		[Fact]
		public void Offset_corner_is_mitred_so_both_legs_stay_at_distance()
		{
			// 90° left turn at (10,0): +X then +Z. Inside of the turn is the left side.
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f) };
			List<Vector3> inside = LineTrace.Offset(line, 1f);
			Assert.Equal(9f, inside[1].x, 3);
			Assert.Equal(1f, inside[1].z, 3);
			List<Vector3> outside = LineTrace.Offset(line, -1f);
			Assert.Equal(11f, outside[1].x, 3);
			Assert.Equal(-1f, outside[1].z, 3);
		}

		[Fact]
		public void Sharp_turn_mitre_is_capped()
		{
			// Near U-turn: the mitre would explode without the cap.
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 0f, 0.2f) };
			List<Vector3> offset = LineTrace.Offset(line, 1f);
			float shift = new Vector2(offset[1].x - 10f, offset[1].z).magnitude;
			Assert.True(shift <= 3.0001f);
		}

		[Fact]
		public void Straight_run_tiles_are_evenly_spaced_with_flush_ends()
		{
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f) };
			List<TraceTile> tiles = LineTrace.Tile(line, 1.6f, 3f);
			Assert.Equal(7, tiles.Count); // ceil(10 / 1.6)
			float spacing = 10f / 7f;
			for (int i = 0; i < tiles.Count; i++)
			{
				Assert.Equal((i + 0.5f) * spacing, tiles[i].Center.x, 3);
				Assert.Equal(new Vector3(1f, 0f, 0f), tiles[i].Direction);
			}
			Assert.True(spacing <= 1.6f);
		}

		[Fact]
		public void Gap_between_any_neighbouring_tiles_never_exceeds_step()
		{
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(3.7f, 0f, 0f), new Vector3(3.7f, 0f, 5.1f) };
			List<TraceTile> tiles = LineTrace.Tile(line, 2f, 100f); // corner tile off
			for (int i = 1; i < tiles.Count; i++)
			{
				if (tiles[i].Direction != tiles[i - 1].Direction) continue; // across the corner is covered separately
				Assert.True((tiles[i].Center - tiles[i - 1].Center).magnitude <= 2f + 1e-3f);
			}
		}

		[Fact]
		public void Bend_adds_one_tile_on_the_vertex_pointing_along_the_bisector()
		{
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(4f, 0f, 4f) };
			List<TraceTile> tiles = LineTrace.Tile(line, 2f, 3f);
			int corner = tiles.FindIndex(t => t.Center == new Vector3(4f, 0f, 0f));
			Assert.True(corner >= 0);
			Assert.Equal(1f / Mathf.Sqrt(2f), tiles[corner].Direction.x, 3);
			Assert.Equal(1f / Mathf.Sqrt(2f), tiles[corner].Direction.z, 3);
			Assert.Equal(5, tiles.Count); // 2 + corner + 2
		}

		[Fact]
		public void Nearly_straight_joints_get_no_corner_tile()
		{
			var line = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(8f, 0f, 0.1f) };
			Assert.Equal(4, LineTrace.Tile(line, 2f, 3f).Count);
		}

		[Fact]
		public void Tile_handles_degenerate_input()
		{
			Assert.Empty(LineTrace.Tile(null, 2f, 3f));
			Assert.Empty(LineTrace.Tile(new List<Vector3> { Vector3.zero }, 2f, 3f));
			Assert.Empty(LineTrace.Tile(new List<Vector3> { Vector3.zero, Vector3.zero }, 2f, 3f));
			Assert.Empty(LineTrace.Tile(new List<Vector3> { Vector3.zero, Vector3.right }, 0f, 3f));
		}

		[Fact]
		public void Empty_and_invalid_inputs_are_safe()
		{
			Assert.Empty(LineTrace.Chain(new List<TraceSegment>(), 0, 0.3f, 0.5f, 10, out _));
			Assert.Empty(LineTrace.Offset(new List<Vector3>(), 1f));
			Assert.Equal(1, LineTrace.SideOf(new List<Vector3>(), Vector3.zero));
		}
	}
}
