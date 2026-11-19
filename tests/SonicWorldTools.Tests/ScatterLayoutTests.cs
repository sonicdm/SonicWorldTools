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

		private static ScatterLayoutOptions Options(int count, float[] weights, int seed = 5) => new ScatterLayoutOptions
		{
			Count = count, Seed = seed, Weights = weights, ScaleMin = 1f, ScaleMax = 1f, Radius = 10f,
		};

		[Fact]
		public void Min_spacing_is_respected_in_world_metres()
		{
			ScatterLayoutOptions options = Options(40, new[] { 1f });
			options.MinSpacing = 2f;
			ScatterSlot[] slots = ScatterLayout.Generate(options);
			Assert.NotEmpty(slots);
			for (int i = 0; i < slots.Length; i++)
				for (int j = i + 1; j < slots.Length; j++)
					Assert.True((slots[i].Offset - slots[j].Offset).magnitude * options.Radius >= 2f - 1e-4f);
		}

		[Fact]
		public void Spacing_that_cannot_fit_places_fewer_rather_than_overlapping()
		{
			ScatterLayoutOptions options = Options(200, new[] { 1f });
			options.MinSpacing = 5f; // a 10 m disk holds only a handful at 5 m apart
			ScatterSlot[] slots = ScatterLayout.Generate(options);
			Assert.InRange(slots.Length, 1, 30);
		}

		[Fact]
		public void Weights_bias_the_picks()
		{
			ScatterSlot[] slots = ScatterLayout.Generate(Options(20000, new[] { 3f, 1f }));
			double first = slots.Count(s => s.SampleIndex == 0) / (double)slots.Length;
			Assert.InRange(first, 0.72, 0.78);
		}

		[Fact]
		public void Zero_weight_sample_is_never_picked()
		{
			ScatterSlot[] slots = ScatterLayout.Generate(Options(2000, new[] { 0f, 1f, 1f }));
			Assert.DoesNotContain(slots, s => s.SampleIndex == 0);
		}

		[Fact]
		public void All_zero_weights_give_empty_layout()
		{
			Assert.Empty(ScatterLayout.Generate(Options(10, new[] { 0f, 0f })));
		}

		[Fact]
		public void Scale_factors_stay_in_range_and_tolerate_swapped_bounds()
		{
			ScatterLayoutOptions options = Options(2000, new[] { 1f });
			options.ScaleMin = 1.3f;
			options.ScaleMax = 0.7f;
			ScatterSlot[] slots = ScatterLayout.Generate(options);
			Assert.All(slots, s => Assert.InRange(s.ScaleFactor, 0.7f, 1.3f));
			Assert.Contains(slots, s => s.ScaleFactor < 0.8f);
			Assert.Contains(slots, s => s.ScaleFactor > 1.2f);
		}

		[Fact]
		public void Key_changes_with_inputs_and_ignores_radius_without_spacing()
		{
			ScatterLayoutOptions a = Options(10, new[] { 1f, 2f });
			ScatterLayoutOptions b = a; b.Radius = 20f;
			Assert.Equal(a.Key(), b.Key());
			b.MinSpacing = 1f;
			Assert.NotEqual(a.Key(), b.Key());
			ScatterLayoutOptions c = a; c.Weights = new[] { 1f, 3f };
			Assert.NotEqual(a.Key(), c.Key());
			ScatterLayoutOptions d = a; d.ScaleMax = 1.5f;
			Assert.NotEqual(a.Key(), d.Key());
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
