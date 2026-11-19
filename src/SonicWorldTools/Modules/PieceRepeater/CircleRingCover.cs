using System.Collections.Generic;
using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>One rigid piece of a circle ring cover: offset from the circle centre (x, z), yaw and anti-flicker lift.</summary>
	internal struct RingPiece
	{
		internal Vector2 Offset;
		/// <summary>Yaw in degrees. The piece's length (L) runs along (sin yaw, cos yaw); its width (W) across.</summary>
		internal float YawDeg;
		internal float LayerBias;
		/// <summary>Ring index from the outside (0 = outer ring), or -1 for the grid core.</summary>
		internal int Ring;
	}

	/// <summary>Summary of a built cover, for diagnostics and the shape card.</summary>
	internal struct RingCoverStats
	{
		internal int RingCount;
		internal List<int> PiecesPerRing;
		internal int CorePieces;
		internal float CoreRadius;
		internal float CoverRatio;
	}

	/// <summary>
	/// Gap-free circle fill for rigid rectangular pieces. Outer rings are computed in closed form so every
	/// point is covered: each ring has enough pieces that neighbouring outer corners meet, and the next ring
	/// starts at the smallest radius the previous ring reaches along its seams. Once rings would clip badly
	/// near the centre, the remaining disc is covered by an axis-aligned grid that tucks under the inner ring.
	/// Overlap is the price of no gaps; small per-piece lifts keep overlapping pieces from flickering.
	/// </summary>
	internal static class CircleRingCover
	{
		/// <summary>Minimum share of a ring's piece area that must cover new ground; below it the grid core takes over.</summary>
		internal const float CoreSwitchEfficiency = 0.8f;
		/// <summary>Rings stop once the outer radius falls below this many piece lengths.</summary>
		internal const float CoreMinRings = 2.5f;
		internal const float RingLayerStep = 0.004f;
		internal const float NeighbourLayerStep = 0.002f;
		internal const float OddClosureLayerStep = 0.001f;

		/// <summary>Builds the cover. Returns false (with output cleared) when it needs more than maxPieces.</summary>
		internal static bool Build(float radius, float length, float width, int maxPieces,
			List<RingPiece> output, out RingCoverStats stats)
		{
			output.Clear();
			stats = new RingCoverStats { PiecesPerRing = new List<int>() };
			if (radius <= 0f || length <= 0.001f || width <= 0.001f) return true;

			var ringOuter = new List<float>();
			var ringCounts = new List<int>();
			float ro = radius;
			while (true)
			{
				if (ro < CoreMinRings * length) break;
				int n = RingCount(ro, length);
				float next = NextOuterRadius(ro, width, n);
				// The outer ring always stays so the rim is round; only inner rings may hand over to the grid.
				if (ringOuter.Count > 0 && RingEfficiency(ro, next, n, length, width) < CoreSwitchEfficiency) break;
				ringOuter.Add(ro);
				ringCounts.Add(n);
				ro = next;
				if (ro <= 0f) break;
			}

			int ringTotal = ringOuter.Count;
			// The grid sits lowest; the outer ring highest so the visible rim is always on top.
			float coreBias = 0f;
			float RingBase(int ring) => (ringTotal - ring) * RingLayerStep;

			for (int ring = 0; ring < ringTotal; ring++)
			{
				float outer = ringOuter[ring];
				int n = ringCounts[ring];
				float centreRadius = outer - width * 0.5f;
				float stagger = (ring & 1) == 1 ? Mathf.PI / n : 0f;
				for (int i = 0; i < n; i++)
				{
					float angle = stagger + 2f * Mathf.PI * i / n;
					float lift = (n & 1) == 1 && i == n - 1 ? OddClosureLayerStep
						: (i & 1) == 0 ? NeighbourLayerStep : 0f;
					output.Add(new RingPiece
					{
						Offset = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * centreRadius,
						YawDeg = angle * Mathf.Rad2Deg + 90f,
						LayerBias = RingBase(ring) + lift,
						Ring = ring,
					});
					if (output.Count > maxPieces) { output.Clear(); return false; }
				}
				stats.PiecesPerRing.Add(n);
			}

			float coreRadius = ringTotal > 0 ? Mathf.Max(0f, ro) : radius;
			if (coreRadius > 0f)
			{
				var centred = BuildGrid(coreRadius, length, width, true);
				var seamed = BuildGrid(coreRadius, length, width, false);
				List<Vector2> core = seamed.Count < centred.Count ? seamed : centred;
				foreach (Vector2 c in core)
				{
					output.Add(new RingPiece { Offset = c, YawDeg = 0f, LayerBias = coreBias, Ring = -1 });
					if (output.Count > maxPieces) { output.Clear(); return false; }
				}
				stats.CorePieces = core.Count;
			}

			stats.RingCount = ringTotal;
			stats.CoreRadius = coreRadius;
			stats.CoverRatio = output.Count * length * width / (Mathf.PI * radius * radius);
			return true;
		}

		/// <summary>Pieces needed so neighbouring outer corners meet: Ro·tan(π/N) ≤ L/2.</summary>
		internal static int RingCount(float outerRadius, float length)
		{
			return Mathf.Max(3, Mathf.CeilToInt(Mathf.PI / Mathf.Atan(length / (2f * outerRadius)) - 1e-4f));
		}

		/// <summary>The smallest radius a ring covers, reached along its seams.</summary>
		internal static float NextOuterRadius(float outerRadius, float width, int count)
		{
			return (outerRadius - width) / Mathf.Cos(Mathf.PI / count);
		}

		/// <summary>Share of the ring's piece area that covers new ground.</summary>
		internal static float RingEfficiency(float outerRadius, float nextOuter, int count, float length, float width)
		{
			float inner = Mathf.Max(0f, nextOuter);
			return Mathf.PI * (outerRadius * outerRadius - inner * inner) / (count * length * width);
		}

		/// <summary>
		/// Axis-aligned cells (length along z, width along x) whose rectangle touches the disc, either with a
		/// piece centred on the middle or with a seam on the middle.
		/// </summary>
		private static List<Vector2> BuildGrid(float radius, float length, float width, bool centred)
		{
			var cells = new List<Vector2>();
			float offset = centred ? 0f : 0.5f;
			int spanX = Mathf.CeilToInt(radius / width) + 1;
			int spanZ = Mathf.CeilToInt(radius / length) + 1;
			for (int iz = -spanZ; iz <= spanZ; iz++)
			{
				float cz = (iz + offset) * length;
				for (int ix = -spanX; ix <= spanX; ix++)
				{
					float cx = (ix + offset) * width;
					float nearX = Mathf.Max(0f, Mathf.Abs(cx) - width * 0.5f);
					float nearZ = Mathf.Max(0f, Mathf.Abs(cz) - length * 0.5f);
					if (nearX * nearX + nearZ * nearZ < radius * radius - 1e-6f)
						cells.Add(new Vector2(cx, cz));
				}
			}
			return cells;
		}
	}
}
