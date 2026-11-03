using System.Collections.Generic;
using System.Linq;
using SonicWorldTools.Core;
using Xunit;

namespace SonicWorldTools.Tests
{
	public sealed class SelectionStateTests
	{
		private static SelectionCandidate Cand(string key, float x, float y, float z)
		{
			return new SelectionCandidate(new SelectionObjectId(key), x, y, z);
		}

		[Fact]
		public void SetCorners_recomputes_BaseSelection_via_query()
		{
			var state = new SelectionState();
			var candidates = new[]
			{
				Cand("a", 1f, 1f, 1f),
				Cand("b", 5f, 1f, 1f),
				Cand("c", 20f, 1f, 1f),
			};

			state.SetCorners(0f, 0f, 0f, 10f, 2f, 2f, candidates);

			Assert.True(state.HasCorners);
			Assert.Equal(0f, state.Volume.MinX);
			Assert.Equal(10f, state.Volume.MaxX);
			Assert.Equal(new[] { "a", "b" }, state.BaseSelection.OrderBy(k => k).ToArray());
			Assert.Empty(state.AddedSelection);
			Assert.Empty(state.ExcludedSelection);
		}

		[Fact]
		public void SetCorners_AB_swap_same_base()
		{
			var state = new SelectionState();
			var candidates = new[]
			{
				Cand("in", 2f, 2f, 2f),
				Cand("out", 50f, 2f, 2f),
			};

			state.SetCorners(0f, 0f, 0f, 4f, 4f, 4f, candidates);
			string[] first = state.BaseSelection.OrderBy(k => k).ToArray();

			state.SetCorners(4f, 4f, 4f, 0f, 0f, 0f, candidates);
			string[] second = state.BaseSelection.OrderBy(k => k).ToArray();

			Assert.Equal(first, second);
			Assert.Equal(new[] { "in" }, second);
		}

		[Fact]
		public void SetCorners_persist_false_clears_prior_refinements()
		{
			var state = new SelectionState();
			state.SetBase(new[] { new SelectionObjectId("old") });
			state.Add(new SelectionObjectId("manual"));
			state.Exclude(new SelectionObjectId("gone"));

			state.SetCorners(
				0f,
				0f,
				0f,
				1f,
				1f,
				1f,
				new[] { Cand("fresh", 0.5f, 0.5f, 0.5f) },
				persistRefinements: false);

			Assert.Equal(new[] { "fresh" }, state.BaseSelection.ToArray());
			Assert.Empty(state.AddedSelection);
			Assert.Empty(state.ExcludedSelection);
			Assert.True(state.ContainsEffective(new SelectionObjectId("fresh")));
			Assert.False(state.ContainsEffective(new SelectionObjectId("manual")));
		}

		[Fact]
		public void SetCorners_with_volume_overload_recomputes_base()
		{
			var state = new SelectionState();
			SelectionVolume volume = SelectionVolume.FromCorners(-1f, -1f, -1f, 1f, 1f, 1f);

			state.SetCorners(volume, new[] { Cand("hit", 0f, 0f, 0f), Cand("miss", 2f, 0f, 0f) });

			Assert.True(state.HasCorners);
			Assert.Equal(new[] { "hit" }, state.BaseSelection.ToArray());
		}

		[Fact]
		public void Clear_resets_volume_and_sets()
		{
			var state = new SelectionState();
			state.SetCorners(0f, 0f, 0f, 2f, 2f, 2f, new[] { Cand("a", 1f, 1f, 1f) });
			state.Clear();

			Assert.False(state.HasCorners);
			Assert.Empty(state.BaseSelection);
			Assert.Empty(state.AddedSelection);
			Assert.Empty(state.ExcludedSelection);
		}

		[Fact]
		public void GetEffectiveSelection_base_only_when_refinements_empty()
		{
			var state = new SelectionState();
			state.SetCorners(0f, 0f, 0f, 5f, 5f, 5f, new[]
			{
				Cand("a", 1f, 1f, 1f),
				Cand("b", 2f, 2f, 2f),
			});

			HashSet<string> effective = state.GetEffectiveSelection();
			Assert.Equal(new HashSet<string> { "a", "b" }, effective);
		}

		[Fact]
		public void Add_and_Exclude_structure_ready_for_M4()
		{
			var state = new SelectionState();
			state.SetBase(new[]
			{
				new SelectionObjectId("a"),
				new SelectionObjectId("b"),
				new SelectionObjectId("c"),
			});

			state.Exclude(new SelectionObjectId("b"));
			state.Add(new SelectionObjectId("d"));

			Assert.True(state.ContainsEffective(new SelectionObjectId("a")));
			Assert.False(state.ContainsEffective(new SelectionObjectId("b")));
			Assert.True(state.ContainsEffective(new SelectionObjectId("c")));
			Assert.True(state.ContainsEffective(new SelectionObjectId("d")));

			HashSet<string> effective = state.GetEffectiveSelection();
			Assert.Equal(new HashSet<string> { "a", "c", "d" }, effective);
		}

		[Fact]
		public void SetCorners_empty_candidates_yields_empty_base()
		{
			var state = new SelectionState();
			state.SetCorners(0f, 0f, 0f, 10f, 10f, 10f, System.Array.Empty<SelectionCandidate>());
			Assert.Empty(state.BaseSelection);
			Assert.True(state.HasCorners);
		}
	}
}
