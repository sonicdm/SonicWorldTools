using System;
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

		internal ScatterSlot(Vector2 offset, float yawDegrees, int sampleIndex)
		{
			Offset = offset;
			YawDegrees = yawDegrees;
			SampleIndex = sampleIndex;
		}
	}

	/// <summary>
	/// Deterministic scatter layout. Kept free of Unity runtime calls so the preview and
	/// the placement share one layout and the math stays unit-testable.
	/// </summary>
	internal static class ScatterLayout
	{
		internal static ScatterSlot[] Generate(int count, int sampleCount, int seed)
		{
			if (count <= 0 || sampleCount <= 0) return Array.Empty<ScatterSlot>();
			var rng = new System.Random(seed);
			var slots = new ScatterSlot[count];
			for (int i = 0; i < count; i++)
			{
				// sqrt keeps the density uniform across the disk instead of bunching at the centre.
				double distance = Math.Sqrt(rng.NextDouble());
				double angle = rng.NextDouble() * Math.PI * 2.0;
				var offset = new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
				float yaw = (float)(rng.NextDouble() * 360.0);
				// Pick a sample independently per object so leftovers are not biased
				// toward the first samples (matches the previous WEC-based behaviour).
				int sample = rng.Next(sampleCount);
				slots[i] = new ScatterSlot(offset, yaw, sample);
			}

			return slots;
		}

		/// <summary>World XZ position for a slot; Y is left at <paramref name="center"/>.y for ground resolution.</summary>
		internal static Vector3 ToWorld(ScatterSlot slot, Vector3 center, float radius)
		{
			return new Vector3(center.x + slot.Offset.x * radius, center.y, center.z + slot.Offset.y * radius);
		}
	}
}
