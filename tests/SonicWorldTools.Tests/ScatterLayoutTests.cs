using System.Linq;
using SonicWorldTools.Modules.ScatterProps;
using UnityEngine;
using Xunit;

namespace SonicWorldTools.Tests
{
	public sealed class ScatterLayoutTests
	{
		[Fact]
		public void Same_seed_gives_identical_layout_so_preview_matches_placement()
		{
			ScatterSlot[] first = ScatterLayout.Generate(50, 3, 1234);
			ScatterSlot[] second = ScatterLayout.Generate(50, 3, 1234);
			Assert.Equal(50, first.Length);
			for (int i = 0; i < first.Length; i++)
			{
				Assert.Equal(first[i].Offset, second[i].Offset);
				Assert.Equal(first[i].YawDegrees, second[i].YawDegrees);
				Assert.Equal(first[i].SampleIndex, second[i].SampleIndex);
			}
		}

		[Fact]
		public void Different_seed_rerolls_the_layout()
		{
			ScatterSlot[] first = ScatterLayout.Generate(20, 2, 1);
			ScatterSlot[] second = ScatterLayout.Generate(20, 2, 2);
			Assert.Contains(Enumerable.Range(0, 20), i => first[i].Offset != second[i].Offset);
		}

		[Fact]
		public void Slots_stay_inside_unit_disk_with_valid_yaw_and_sample()
		{
			foreach (ScatterSlot slot in ScatterLayout.Generate(2000, 4, 42))
			{
				Assert.True(slot.Offset.sqrMagnitude <= 1.0001f);
				Assert.InRange(slot.YawDegrees, 0f, 360f);
				Assert.InRange(slot.SampleIndex, 0, 3);
			}
		}

		[Fact]
		public void Density_is_uniform_not_bunched_at_centre()
		{
			// Uniform over area: about a quarter of points fall within half the radius.
			ScatterSlot[] slots = ScatterLayout.Generate(20000, 1, 7);
			double inner = slots.Count(s => s.Offset.magnitude <= 0.5f) / (double)slots.Length;
			Assert.InRange(inner, 0.23, 0.27);
		}

		[Fact]
		public void Every_sample_type_is_used()
		{
			ScatterSlot[] slots = ScatterLayout.Generate(400, 5, 99);
			for (int sample = 0; sample < 5; sample++)
				Assert.Contains(slots, s => s.SampleIndex == sample);
		}

		[Fact]
		public void Empty_inputs_give_empty_layout()
		{
			Assert.Empty(ScatterLayout.Generate(0, 3, 1));
			Assert.Empty(ScatterLayout.Generate(10, 0, 1));
		}

		[Fact]
		public void ToWorld_scales_offset_by_radius_around_centre()
		{
			var slot = new ScatterSlot(new Vector2(0.5f, -1f), 0f, 0);
			Vector3 world = ScatterLayout.ToWorld(slot, new Vector3(10f, 3f, 20f), 4f);
			Assert.Equal(new Vector3(12f, 3f, 16f), world);
		}
	}
}
