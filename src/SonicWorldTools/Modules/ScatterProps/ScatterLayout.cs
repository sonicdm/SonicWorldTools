using System;
using System.Collections.Generic;
using UnityEngine;

namespace SonicWorldTools.Modules.ScatterProps
{
	/// <summary>One object in a scatter layout, relative to a unit-radius disk.</summary>
	internal readonly struct ScatterSlot
	{
		/// <summary>Offset inside the unit disk (x → world X, y → world Z).</summary>
		internal readonly Vector2 Offset;
		internal readonly float YawDegrees;
		internal readonly int SampleIndex;
		/// <summary>Multiplier on the sample's scale (1 = sampled size).</summary>
		internal readonly float ScaleFactor;

		internal ScatterSlot(Vector2 offset, float yawDegrees, int sampleIndex, float scaleFactor = 1f)
		{
			Offset = offset;
			YawDegrees = yawDegrees;
			SampleIndex = sampleIndex;
			ScaleFactor = scaleFactor;
		}
	}

	/// <summary>Inputs that shape a scatter layout. Any change produces a different layout.</summary>
	internal struct ScatterLayoutOptions
	{
		internal int Count;
		internal int Seed;
		/// <summary>Relative pick weight per palette entry; length is the palette size.</summary>
		internal float[] Weights;
		/// <summary>Minimum distance between objects in metres (0 = off).</summary>
		internal float MinSpacing;
		/// <summary>Radius in metres; only used to convert <see cref="MinSpacing"/> to disk units.</summary>
		internal float Radius;
		internal float ScaleMin;
		internal float ScaleMax;

		/// <summary>Stable key so callers can tell when the layout must be regenerated.</summary>
		internal string Key()
		{
			string weights = Weights == null ? "" : string.Join(",", Array.ConvertAll(Weights, w => w.ToString("R")));
			// Radius only shapes the layout when spacing is on; otherwise resizing just scales it.
			float radius = MinSpacing > 0f ? Radius : 0f;
			return Count + "|" + Seed + "|" + weights + "|" + MinSpacing.ToString("R") + "|" + radius.ToString("R")
			       + "|" + ScaleMin.ToString("R") + "|" + ScaleMax.ToString("R");
		}
	}

	/// <summary>
	/// Deterministic scatter layout. Kept free of Unity runtime calls so the preview and
	/// the placement share one layout and the math stays unit-testable.
	/// </summary>
	internal static class ScatterLayout
	{
		/// <summary>Candidate draws per object before giving up on fitting it under the spacing rule.</summary>
		internal const int SpacingAttempts = 30;

		internal static ScatterSlot[] Generate(int count, int sampleCount, int seed)
		{
			if (sampleCount <= 0) return Array.Empty<ScatterSlot>();
			var weights = new float[sampleCount];
			for (int i = 0; i < sampleCount; i++) weights[i] = 1f;
			return Generate(new ScatterLayoutOptions { Count = count, Seed = seed, Weights = weights, ScaleMin = 1f, ScaleMax = 1f });
		}

		internal static ScatterSlot[] Generate(ScatterLayoutOptions options)
		{
			float[] weights = options.Weights;
			if (options.Count <= 0 || weights == null || weights.Length == 0) return Array.Empty<ScatterSlot>();
			double totalWeight = 0;
			for (int i = 0; i < weights.Length; i++) totalWeight += Math.Max(0f, weights[i]);
			if (totalWeight <= 0) return Array.Empty<ScatterSlot>();

			float scaleMin = Math.Max(0.01f, Math.Min(options.ScaleMin, options.ScaleMax));
			float scaleMax = Math.Max(scaleMin, Math.Max(options.ScaleMin, options.ScaleMax));
			// Spacing in unit-disk terms; a radius of 0 cannot hold spaced objects, so treat spacing as off.
			double spacing = options.MinSpacing > 0f && options.Radius > 0f ? options.MinSpacing / options.Radius : 0.0;
			double spacingSq = spacing * spacing;

			var rng = new System.Random(options.Seed);
			var slots = new List<ScatterSlot>(options.Count);
			for (int i = 0; i < options.Count; i++)
			{
				// Draw every random value for this object up front so the RNG stream stays
				// aligned whether or not the spacing rule rejects candidates.
				float yaw = (float)(rng.NextDouble() * 360.0);
				int sample = PickWeighted(rng.NextDouble() * totalWeight, weights);
				float scale = scaleMin + (float)rng.NextDouble() * (scaleMax - scaleMin);

				bool placed = false;
				Vector2 offset = default;
				int attempts = spacing > 0 ? SpacingAttempts : 1;
				for (int attempt = 0; attempt < attempts && !placed; attempt++)
				{
					// sqrt keeps the density uniform across the disk instead of bunching at the centre.
					double distance = Math.Sqrt(rng.NextDouble());
					double angle = rng.NextDouble() * Math.PI * 2.0;
					offset = new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
					placed = spacing <= 0 || IsFarEnough(offset, slots, spacingSq);
				}

				if (placed) slots.Add(new ScatterSlot(offset, yaw, sample, scale));
			}

			return slots.ToArray();
		}

		private static int PickWeighted(double roll, float[] weights)
		{
			for (int i = 0; i < weights.Length; i++)
			{
				double w = Math.Max(0f, weights[i]);
				if (roll < w) return i;
				roll -= w;
			}

			// Floating-point leftovers land on the last entry with a positive weight.
			for (int i = weights.Length - 1; i >= 0; i--)
				if (weights[i] > 0f) return i;
			return 0;
		}

		private static bool IsFarEnough(Vector2 candidate, List<ScatterSlot> slots, double spacingSq)
		{
			for (int i = 0; i < slots.Count; i++)
			{
				double dx = candidate.x - slots[i].Offset.x;
				double dy = candidate.y - slots[i].Offset.y;
				if (dx * dx + dy * dy < spacingSq) return false;
			}
			return true;
		}

		/// <summary>World XZ position for a slot; Y is left at <paramref name="center"/>.y for ground resolution.</summary>
		internal static Vector3 ToWorld(ScatterSlot slot, Vector3 center, float radius)
		{
			return new Vector3(center.x + slot.Offset.x * radius, center.y, center.z + slot.Offset.y * radius);
		}

		/// <summary>
		/// Rotation for a slot: yaw about world up, then tilted toward <paramref name="groundNormal"/>
		/// by at most <paramref name="maxTiltDegrees"/> (0 = upright).
		/// </summary>
		internal static Quaternion Orientation(float yawDegrees, Vector3 groundNormal, float maxTiltDegrees)
		{
			Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
			if (maxTiltDegrees <= 0f || groundNormal.sqrMagnitude < 1e-6f) return yaw;
			Vector3 normal = groundNormal.normalized;
			if (normal.y <= 0f) return yaw;
			Vector3 up = Vector3.RotateTowards(Vector3.up, normal, maxTiltDegrees * Mathf.Deg2Rad, 0f);
			return Quaternion.FromToRotation(Vector3.up, up) * yaw;
		}
	}
}
