using System.Collections.Generic;
using System.Linq;
using SonicWorldTools.Core;
using SonicWorldTools.Refinement;
using Xunit;

namespace SonicWorldTools.Tests
{
	/// <summary>Build-plan §16 refinement scenarios + persistence / reset.</summary>
	public sealed class RefinementTests
	{
		private static SelectionObjectId Id(string key)
		{
			return new SelectionObjectId(key);
		}

		private static SelectionCandidate Cand(string key, float x, float y, float z)
		{
			return new SelectionCandidate(Id(key), x, y, z);
		}

		[Fact]
		public void Exclude_B_from_ABC_effective_is_AC()
		{
			var refinement = new SelectionRefinement();
			refinement.SetBase(new[] { "A", "B", "C" });
			refinement.Exclude("B");

			Assert.Equal(new HashSet<string> { "A", "C" }, refinement.GetEffectiveSelection());
			Assert.Equal(new[] { "B" }, refinement.ExcludedSelection.ToArray());
			Assert.Empty(refinement.AddedSelection);
		}

		[Fact]
		public void Add_D_to_ABC_effective_is_ABCD()
		{
			var refinement = new SelectionRefinement();
			refinement.SetBase(new[] { "A", "B", "C" });
			refinement.Add("D");

			Assert.Equal(new HashSet<string> { "A", "B", "C", "D" }, refinement.GetEffectiveSelection());
			Assert.Equal(new[] { "D" }, refinement.AddedSelection.ToArray());
			Assert.Empty(refinement.ExcludedSelection);
		}

		[Fact]
		public void Recompute_persists_excluded_B_and_added_D_effective_ACDE()
		{
			// §16: base → A,C,E; excluded B remains harmless; added D persists; effective = A,C,D,E
			var state = new SelectionState();
			state.SetBase(new[] { Id("A"), Id("B"), Id("C") });
			state.Exclude(Id("B"));
			state.Add(Id("D"));

			Assert.Equal(new HashSet<string> { "A", "C", "D" }, state.GetEffectiveSelection());

			// New volume contains A,C,E; candidates still include B and D (still valid in world).
			var candidates = new[]
			{
				Cand("A", 1f, 1f, 1f),
				Cand("B", 50f, 1f, 1f),
				Cand("C", 2f, 1f, 1f),
				Cand("D", 60f, 1f, 1f),
				Cand("E", 3f, 1f, 1f),
			};

			state.SetCorners(0f, 0f, 0f, 4f, 2f, 2f, candidates, persistRefinements: true);

			Assert.Equal(new[] { "A", "C", "E" }, state.BaseSelection.OrderBy(k => k).ToArray());
			Assert.Equal(new[] { "B" }, state.ExcludedSelection.ToArray());
			Assert.Equal(new[] { "D" }, state.AddedSelection.ToArray());
			Assert.Equal(new HashSet<string> { "A", "C", "D", "E" }, state.GetEffectiveSelection());
		}

		[Fact]
		public void Recompute_without_persist_clears_refinements()
		{
			var state = new SelectionState();
			state.SetBase(new[] { Id("A"), Id("B"), Id("C") });
			state.Exclude(Id("B"));
			state.Add(Id("D"));

			var candidates = new[]
			{
				Cand("A", 1f, 1f, 1f),
				Cand("B", 50f, 1f, 1f),
				Cand("C", 2f, 1f, 1f),
				Cand("D", 60f, 1f, 1f),
				Cand("E", 3f, 1f, 1f),
			};

			state.SetCorners(0f, 0f, 0f, 4f, 2f, 2f, candidates, persistRefinements: false);

			Assert.Equal(new[] { "A", "C", "E" }, state.BaseSelection.OrderBy(k => k).ToArray());
			Assert.Empty(state.AddedSelection);
			Assert.Empty(state.ExcludedSelection);
			Assert.Equal(new HashSet<string> { "A", "C", "E" }, state.GetEffectiveSelection());
		}

		[Fact]
		public void Persist_prunes_refinements_for_missing_candidates()
		{
			var state = new SelectionState();
			state.SetBase(new[] { Id("A"), Id("B") });
			state.Exclude(Id("B"));
			state.Add(Id("D"));

			// B and D gone from world candidates — pruned; only A remains in new base.
			var candidates = new[]
			{
				Cand("A", 1f, 1f, 1f),
				Cand("E", 2f, 1f, 1f),
			};

			state.SetCorners(0f, 0f, 0f, 4f, 2f, 2f, candidates, persistRefinements: true);

			Assert.Equal(new[] { "A", "E" }, state.BaseSelection.OrderBy(k => k).ToArray());
			Assert.Empty(state.AddedSelection);
			Assert.Empty(state.ExcludedSelection);
			Assert.Equal(new HashSet<string> { "A", "E" }, state.GetEffectiveSelection());
		}

		[Fact]
		public void ResetRefinement_clears_added_and_excluded_keeps_base()
		{
			var state = new SelectionState();
			state.SetBase(new[] { Id("A"), Id("B"), Id("C") });
			state.Exclude(Id("B"));
			state.Add(Id("D"));

			state.ResetRefinement();

			Assert.Equal(new[] { "A", "B", "C" }, state.BaseSelection.OrderBy(k => k).ToArray());
			Assert.Empty(state.AddedSelection);
			Assert.Empty(state.ExcludedSelection);
			Assert.Equal(new HashSet<string> { "A", "B", "C" }, state.GetEffectiveSelection());
		}

		[Fact]
		public void Add_after_Exclude_restores_membership()
		{
			var refinement = new SelectionRefinement();
			refinement.SetBase(new[] { "A", "B" });
			refinement.Exclude("B");
			Assert.False(refinement.ContainsEffective("B"));

			refinement.Add("B");
			Assert.True(refinement.ContainsEffective("B"));
			Assert.DoesNotContain("B", refinement.ExcludedSelection);
			Assert.Contains("B", refinement.AddedSelection);
		}

		[Fact]
		public void Exclude_after_Add_removes_from_added()
		{
			var refinement = new SelectionRefinement();
			refinement.SetBase(new[] { "A" });
			refinement.Add("D");
			refinement.Exclude("D");

			Assert.False(refinement.ContainsEffective("D"));
			Assert.Empty(refinement.AddedSelection);
			Assert.Contains("D", refinement.ExcludedSelection);
		}

		[Fact]
		public void DropKeys_removes_from_base_added_and_excluded()
		{
			var refinement = new SelectionRefinement();
			refinement.SetBase(new[] { "A", "B", "C" });
			refinement.Add("D");
			refinement.Exclude("B");

			refinement.DropKeys(new[] { "B", "D", "missing" });

			Assert.Equal(new HashSet<string> { "A", "C" }, refinement.GetEffectiveSelection());
			Assert.Empty(refinement.AddedSelection);
			Assert.Empty(refinement.ExcludedSelection);
			Assert.DoesNotContain("B", refinement.BaseSelection);
		}

		[Fact]
		public void GetEffectiveSelection_returns_new_HashSet_of_string_keys()
		{
			var refinement = new SelectionRefinement();
			refinement.SetBase(new[] { "A", "B" });
			refinement.Exclude("B");
			refinement.Add("C");

			HashSet<string> first = refinement.GetEffectiveSelection();
			HashSet<string> second = refinement.GetEffectiveSelection();

			Assert.Equal(new HashSet<string> { "A", "C" }, first);
			Assert.Equal(first, second);
			Assert.False(ReferenceEquals(first, second));
		}

		[Fact]
		public void SelectionState_GetEffectiveSelection_delegates_to_refinement()
		{
			var state = new SelectionState();
			state.SetBase(new[] { Id("wall-1"), Id("wall-2") });
			state.Exclude(Id("wall-2"));
			state.Add(Id("adjacent"));

			HashSet<string> effective = state.GetEffectiveSelection();
			Assert.Equal(new HashSet<string> { "wall-1", "adjacent" }, effective);
			Assert.Equal(effective, state.Refinement.GetEffectiveSelection());
		}
	}
}
