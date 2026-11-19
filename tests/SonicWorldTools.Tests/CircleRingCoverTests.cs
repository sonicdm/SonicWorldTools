using System.Collections.Generic;
using SonicWorldTools.Modules.PieceRepeater;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace SonicWorldTools.Tests
{
	public sealed class CircleRingCoverTests
	{
		private readonly ITestOutputHelper _log;
		public CircleRingCoverTests(ITestOutputHelper log) { _log = log; }

		public static IEnumerable<object[]> PieceSizes()
		{
			yield return new object[] { 2f, 2f };
			yield return new object[] { 2f, 1f };
			yield return new object[] { 2.1f, 2.1f };
			yield return new object[] { 4f, 2f };
		}

		[Theory]
		[MemberData(nameof(PieceSizes))]
		public void Every_point_of_the_disc_is_covered(float length, float width)
		{
			var pieces = new List<RingPiece>();
			for (float radius = 1f; radius <= 20f; radius += 0.37f)
			{
				Assert.True(CircleRingCover.Build(radius, length, width, 100000, pieces, out _));
				const float step = 0.08f;
				for (float x = -radius; x <= radius; x += step)
				for (float z = -radius; z <= radius; z += step)
				{
					if (x * x + z * z > (radius - 0.001f) * (radius - 0.001f)) continue;
					Assert.True(IsCovered(pieces, new Vector2(x, z), length, width),
						$"gap at ({x:0.00},{z:0.00}) for R={radius:0.00} L={length} W={width}");
				}
			}
		}

		[Theory]
		[MemberData(nameof(PieceSizes))]
		public void Overlap_stays_bounded_for_larger_circles(float length, float width)
		{
			var pieces = new List<RingPiece>();
			for (float radius = 3f * length; radius <= 30f; radius += 0.37f)
			{
				Assert.True(CircleRingCover.Build(radius, length, width, 100000, pieces, out RingCoverStats stats));
				_log.WriteLine($"R={radius:0.00} rings={stats.RingCount} [{string.Join(",", stats.PiecesPerRing)}] core={stats.CorePieces} coreR={stats.CoreRadius:0.00} ratio={stats.CoverRatio:0.000}");
				float bound = radius >= 6f * length ? 1.25f : 1.5f;
				Assert.True(stats.CoverRatio < bound, $"cover ratio {stats.CoverRatio:0.000} at R={radius:0.00}");
			}
		}

		[Fact]
		public void No_ring_is_placed_below_the_switch_efficiency()
		{
			var pieces = new List<RingPiece>();
			for (float radius = 1f; radius <= 30f; radius += 0.37f)
			{
				CircleRingCover.Build(radius, 2f, 2f, 100000, pieces, out RingCoverStats stats);
				float ro = radius;
				for (int ring = 0; ring < stats.RingCount; ring++)
				{
					int n = stats.PiecesPerRing[ring];
					float next = CircleRingCover.NextOuterRadius(ro, 2f, n);
					// The outer ring is always kept so the rim stays round.
					if (ring > 0)
						Assert.True(CircleRingCover.RingEfficiency(ro, next, n, 2f, 2f) >= CircleRingCover.CoreSwitchEfficiency);
					ro = next;
				}
			}
		}

		[Fact]
		public void Neighbours_in_a_ring_never_share_a_layer()
		{
			var pieces = new List<RingPiece>();
			for (float radius = 5f; radius <= 30f; radius += 0.37f)
			{
				CircleRingCover.Build(radius, 2f, 2f, 100000, pieces, out RingCoverStats stats);
				int index = 0;
				for (int ring = 0; ring < stats.RingCount; ring++)
				{
					int n = stats.PiecesPerRing[ring];
					for (int i = 0; i < n; i++)
					{
						float a = pieces[index + i].LayerBias;
						float b = pieces[index + (i + 1) % n].LayerBias;
						Assert.NotEqual(a, b);
					}
					index += n;
				}
			}
		}

		[Fact]
		public void Over_limit_returns_false_and_clears()
		{
			var pieces = new List<RingPiece>();
			Assert.False(CircleRingCover.Build(30f, 2f, 2f, 50, pieces, out _));
			Assert.Empty(pieces);
		}

		private static bool IsCovered(List<RingPiece> pieces, Vector2 point, float length, float width)
		{
			const float eps = 1e-3f;
			foreach (RingPiece piece in pieces)
			{
				float yaw = piece.YawDeg * Mathf.Deg2Rad;
				var along = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
				var across = new Vector2(along.y, -along.x);
				Vector2 d = point - piece.Offset;
				if (Mathf.Abs(Vector2.Dot(d, along)) <= length * 0.5f + eps
				    && Mathf.Abs(Vector2.Dot(d, across)) <= width * 0.5f + eps)
					return true;
			}
			return false;
		}
	}
}
