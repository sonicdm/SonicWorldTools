using System.Collections.Generic;
using System.Linq;
using SonicWorldTools.Core;
using Xunit;

namespace SonicWorldTools.Tests
{
	public sealed class SelectionVolumeTests
	{
		[Fact]
		public void Normalize_orders_corners_independently_per_axis()
		{
			SelectionVolumeMath.Normalize(
				10f,
				-2f,
				5f,
				1f,
				3f,
				-1f,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			Assert.Equal(1f, minX);
			Assert.Equal(-2f, minY);
			Assert.Equal(-1f, minZ);
			Assert.Equal(10f, maxX);
			Assert.Equal(3f, maxY);
			Assert.Equal(5f, maxZ);
		}

		[Fact]
		public void Normalize_is_idempotent_when_already_ordered()
		{
			SelectionVolumeMath.Normalize(
				0f,
				0f,
				0f,
				4f,
				8f,
				2f,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			Assert.Equal(0f, minX);
			Assert.Equal(0f, minY);
			Assert.Equal(0f, minZ);
			Assert.Equal(4f, maxX);
			Assert.Equal(8f, maxY);
			Assert.Equal(2f, maxZ);
		}

		[Fact]
		public void FromCorners_AB_ordering_does_not_matter()
		{
			SelectionVolume ab = SelectionVolume.FromCorners(10f, 2f, 8f, 1f, 5f, 3f);
			SelectionVolume ba = SelectionVolume.FromCorners(1f, 5f, 3f, 10f, 2f, 8f);

			Assert.Equal(ab.MinX, ba.MinX);
			Assert.Equal(ab.MinY, ba.MinY);
			Assert.Equal(ab.MinZ, ba.MinZ);
			Assert.Equal(ab.MaxX, ba.MaxX);
			Assert.Equal(ab.MaxY, ba.MaxY);
			Assert.Equal(ab.MaxZ, ba.MaxZ);
		}

		[Fact]
		public void FromCorners_supports_negative_coordinates()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(-10f, -4f, -2f, -1f, -8f, 3f);

			Assert.Equal(-10f, volume.MinX);
			Assert.Equal(-8f, volume.MinY);
			Assert.Equal(-2f, volume.MinZ);
			Assert.Equal(-1f, volume.MaxX);
			Assert.Equal(-4f, volume.MaxY);
			Assert.Equal(3f, volume.MaxZ);
			Assert.True(volume.Contains(-5f, -6f, 0f));
		}

		[Fact]
		public void Contains_includes_exact_boundary_points_inclusive()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(0f, 0f, 0f, 4f, 2f, 6f);

			Assert.True(volume.Contains(0f, 0f, 0f));
			Assert.True(volume.Contains(4f, 2f, 6f));
			Assert.True(volume.Contains(0f, 1f, 3f));
			Assert.True(volume.Contains(4f, 1f, 3f));
			Assert.True(volume.Contains(2f, 0f, 3f));
			Assert.True(volume.Contains(2f, 2f, 3f));
			Assert.True(volume.Contains(2f, 1f, 0f));
			Assert.True(volume.Contains(2f, 1f, 6f));
		}

		[Fact]
		public void Contains_excludes_points_just_outside_each_face()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(0f, 0f, 0f, 4f, 2f, 6f);

			Assert.False(volume.Contains(-0.001f, 1f, 3f));
			Assert.False(volume.Contains(4.001f, 1f, 3f));
			Assert.False(volume.Contains(2f, -0.001f, 3f));
			Assert.False(volume.Contains(2f, 2.001f, 3f));
			Assert.False(volume.Contains(2f, 1f, -0.001f));
			Assert.False(volume.Contains(2f, 1f, 6.001f));
		}

		[Fact]
		public void Contains_zero_width_box_only_plane_points()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(5f, 0f, 0f, 5f, 2f, 4f);

			Assert.Equal(0f, volume.SizeX);
			Assert.True(volume.Contains(5f, 1f, 2f));
			Assert.False(volume.Contains(5.001f, 1f, 2f));
			Assert.False(volume.Contains(4.999f, 1f, 2f));
		}

		[Fact]
		public void Contains_zero_height_box_only_plane_points()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(0f, 3f, 0f, 4f, 3f, 6f);

			Assert.Equal(0f, volume.SizeY);
			Assert.True(volume.Contains(2f, 3f, 3f));
			Assert.False(volume.Contains(2f, 3.001f, 3f));
			Assert.False(volume.Contains(2f, 2.999f, 3f));
		}

		[Fact]
		public void Contains_very_large_box()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(-1e6f, -1e6f, -1e6f, 1e6f, 1e6f, 1e6f);

			Assert.True(volume.Contains(0f, 0f, 0f));
			Assert.True(volume.Contains(999999f, -999999f, 500000f));
			Assert.False(volume.Contains(1e6f + 1f, 0f, 0f));
		}

		[Fact]
		public void Query_returns_only_candidates_inside_volume_origin_mode()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(0f, 0f, 0f, 10f, 5f, 10f);
			var candidates = new[]
			{
				new SelectionCandidate(new SelectionObjectId("inside"), 5f, 2f, 5f),
				new SelectionCandidate(new SelectionObjectId("boundary"), 0f, 0f, 0f),
				new SelectionCandidate(new SelectionObjectId("outside"), 11f, 2f, 5f),
				new SelectionCandidate(new SelectionObjectId("below"), 5f, -1f, 5f),
			};

			IReadOnlyCollection<SelectionObjectId> hits = SelectionQuery.Query(volume, candidates);
			string[] keys = hits.Select(id => id.Key).OrderBy(k => k).ToArray();

			Assert.Equal(new[] { "boundary", "inside" }, keys);
		}

		[Fact]
		public void Query_null_candidates_returns_empty()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(0f, 0f, 0f, 1f, 1f, 1f);
			IReadOnlyCollection<SelectionObjectId> hits = SelectionQuery.Query(volume, null);
			Assert.Empty(hits);
		}

		[Fact]
		public void Size_matches_normalized_extents()
		{
			SelectionVolume volume = SelectionVolume.FromCorners(2f, 8f, 1f, 5f, 3f, 4f);
			Assert.Equal(3f, volume.SizeX);
			Assert.Equal(5f, volume.SizeY);
			Assert.Equal(3f, volume.SizeZ);
		}

		[Fact]
		public void FromThreePoints_axis_aligned_sets_oriented_extents()
		{
			// A→B along +X (width 10); C at +Z depth 4 and Y=5.
			SelectionVolume volume = SelectionVolume.FromThreePoints(
				0f,
				0f,
				0f,
				10f,
				0f,
				0f,
				0f,
				5f,
				4f);

			Assert.True(volume.IsOriented);
			Assert.Equal(10f, volume.SizeX, 3);
			Assert.Equal(5f, volume.SizeY, 3);
			Assert.Equal(4f, volume.SizeZ, 3);
			Assert.Equal(0f, volume.Yaw, 3);
			Assert.True(volume.Contains(5f, 2.5f, 2f));
			Assert.False(volume.Contains(5f, 2.5f, 4.5f));
			Assert.False(volume.Contains(-0.5f, 2.5f, 2f));
		}

		[Fact]
		public void FromThreePoints_rotated_width_edge_contains_interior()
		{
			// A→B along +Z (width 8); C off to -X (depth on +localZ for yaw=π/2).
			SelectionVolume volume = SelectionVolume.FromThreePoints(
				0f,
				1f,
				0f,
				0f,
				1f,
				8f,
				-3f,
				4f,
				0f);

			Assert.True(volume.IsOriented);
			Assert.Equal(8f, volume.SizeX, 3);
			Assert.Equal(3f, volume.SizeY, 3);
			Assert.Equal(3f, volume.SizeZ, 3);
			// Interior at local (4, 1.5, 1.5) → world (-1.5, 2.5, 4).
			Assert.True(volume.Contains(-1.5f, 2.5f, 4f));
			Assert.True(volume.Contains(-1.5f, 2.5f, 2f));
			Assert.False(volume.Contains(3f, 2f, 4f));
		}

		[Fact]
		public void FromThreePoints_depth_on_either_side_of_width_keeps_ab_inside()
		{
			// Same A→B; C on +Z vs −Z must both contain the width edge (no yaw flip).
			SelectionVolume plus = SelectionVolume.FromThreePoints(
				0f, 0f, 0f, 10f, 0f, 0f, 0f, 2f, 4f);
			SelectionVolume minus = SelectionVolume.FromThreePoints(
				0f, 0f, 0f, 10f, 0f, 0f, 0f, 2f, -4f);

			Assert.Equal(0f, plus.Yaw, 3);
			Assert.Equal(0f, minus.Yaw, 3);
			Assert.Equal(10f, plus.SizeX, 3);
			Assert.Equal(10f, minus.SizeX, 3);
			Assert.Equal(4f, plus.SizeZ, 3);
			Assert.Equal(4f, minus.SizeZ, 3);

			Assert.True(plus.Contains(0f, 1f, 0f));
			Assert.True(plus.Contains(10f, 1f, 0f));
			Assert.True(plus.Contains(5f, 1f, 2f));
			Assert.True(minus.Contains(0f, 1f, 0f));
			Assert.True(minus.Contains(10f, 1f, 0f));
			Assert.True(minus.Contains(5f, 1f, -2f));
			Assert.False(minus.Contains(5f, 1f, 2f));
		}

		[Fact]
		public void Query_oriented_volume_filters_by_obb_not_world_aabb()
		{
			SelectionVolume volume = SelectionVolume.FromThreePoints(
				0f,
				0f,
				0f,
				10f,
				0f,
				0f,
				0f,
				2f,
				4f);
			var candidates = new[]
			{
				new SelectionCandidate(new SelectionObjectId("inside"), 5f, 1f, 2f),
				// Inside world AABB of the OBB but outside the rotated box (beyond depth).
				new SelectionCandidate(new SelectionObjectId("aabb_only"), 5f, 1f, 4.5f),
				new SelectionCandidate(new SelectionObjectId("outside"), 12f, 1f, 2f),
			};

			IReadOnlyCollection<SelectionObjectId> hits = SelectionQuery.Query(volume, candidates);
			string[] keys = hits.Select(id => id.Key).OrderBy(k => k).ToArray();

			Assert.Equal(new[] { "inside" }, keys);
		}
	}
}
