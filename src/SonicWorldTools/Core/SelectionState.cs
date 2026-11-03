using System.Collections.Generic;
using SonicWorldTools.Refinement;

namespace SonicWorldTools.Core
{
	/// <summary>
	/// Volume corners plus refinement sets. Corner changes recompute <see cref="BaseSelection"/>
	/// via <see cref="SelectionQuery"/> and optionally persist valid Added/Excluded (§7).
	/// </summary>
	public sealed class SelectionState
	{
		private readonly SelectionRefinement _refinement = new SelectionRefinement();

		public SelectionVolume Volume { get; private set; }

		public bool HasCorners { get; private set; }

		/// <summary>ZDOID-backed keys from the last volume query (M3 maps ZDOID → string).</summary>
		public IReadOnlyCollection<string> BaseSelection => _refinement.BaseSelection;

		public IReadOnlyCollection<string> AddedSelection => _refinement.AddedSelection;

		public IReadOnlyCollection<string> ExcludedSelection => _refinement.ExcludedSelection;

		/// <summary>Underlying refinement sets (add/exclude/reset / effective).</summary>
		public SelectionRefinement Refinement => _refinement;

		public void Clear()
		{
			_refinement.ClearAll();
			Volume = default;
			HasCorners = false;
		}

		/// <summary>
		/// Drop volume/base for a new A→B→C pass while keeping Added/Excluded (Home resize persist).
		/// </summary>
		public void ClearVolumeKeepRefinements()
		{
			_refinement.ReplaceBase(null);
			Volume = default;
			HasCorners = false;
		}

		/// <summary>
		/// Sets corners A/B, normalizes volume, and recomputes base from candidates.
		/// When <paramref name="persistRefinements"/> is true (default; config PersistRefinementsAfterResize),
		/// keeps Added/Excluded keys that still appear among candidates.
		/// </summary>
		public void SetCorners(
			float ax,
			float ay,
			float az,
			float bx,
			float by,
			float bz,
			IEnumerable<SelectionCandidate> candidates,
			bool persistRefinements = true)
		{
			Volume = SelectionVolume.FromCorners(ax, ay, az, bx, by, bz);
			HasCorners = true;
			RecomputeBase(candidates, persistRefinements);
		}

		/// <summary>Three-corner Y-up oriented volume (A→B width, C depth/height).</summary>
		public void SetThreeCorners(
			float ax,
			float ay,
			float az,
			float bx,
			float by,
			float bz,
			float cx,
			float cy,
			float cz,
			IEnumerable<SelectionCandidate> candidates,
			bool persistRefinements = true)
		{
			Volume = SelectionVolume.FromThreePoints(ax, ay, az, bx, by, bz, cx, cy, cz);
			HasCorners = true;
			RecomputeBase(candidates, persistRefinements);
		}

		public void SetCorners(
			SelectionVolume volume,
			IEnumerable<SelectionCandidate> candidates,
			bool persistRefinements = true)
		{
			Volume = volume;
			HasCorners = true;
			RecomputeBase(candidates, persistRefinements);
		}

		/// <summary>Replace base set directly (tests / adapters). Clears refinements.</summary>
		public void SetBase(IEnumerable<SelectionObjectId> ids)
		{
			if (ids == null)
			{
				_refinement.SetBase(null);
				return;
			}

			var keys = new List<string>();
			foreach (SelectionObjectId id in ids)
			{
				keys.Add(id.Key);
			}

			_refinement.SetBase(keys);
		}

		public void Add(SelectionObjectId id)
		{
			_refinement.Add(id.Key);
		}

		public void Exclude(SelectionObjectId id)
		{
			_refinement.Exclude(id.Key);
		}

		/// <summary>Clears Added + Excluded (§7 Reset Refinement).</summary>
		public void ResetRefinement()
		{
			_refinement.ResetRefinement();
		}

		/// <summary>Drop unloaded / invalid keys from all sets before hand-off (§21).</summary>
		public void DropKeys(IEnumerable<string> keys)
		{
			_refinement.DropKeys(keys);
		}

		public bool ContainsEffective(SelectionObjectId id)
		{
			return _refinement.ContainsEffective(id.Key);
		}

		/// <summary>
		/// Frozen for M5: effective = (Base ∪ Added) − Excluded as HashSet of ZDOID.ToString() keys.
		/// </summary>
		public HashSet<string> GetEffectiveSelection()
		{
			return _refinement.GetEffectiveSelection();
		}

		private void RecomputeBase(IEnumerable<SelectionCandidate> candidates, bool persistRefinements)
		{
			IReadOnlyCollection<SelectionObjectId> hits = SelectionQuery.Query(Volume, candidates);
			var newBase = new List<string>(hits.Count);
			foreach (SelectionObjectId id in hits)
			{
				newBase.Add(id.Key);
			}

			if (!persistRefinements)
			{
				_refinement.SetBase(newBase);
				return;
			}

			_refinement.ReplaceBase(newBase);
			_refinement.PruneRefinementsToValid(CollectCandidateKeys(candidates));
		}

		private static IEnumerable<string> CollectCandidateKeys(IEnumerable<SelectionCandidate> candidates)
		{
			if (candidates == null)
			{
				yield break;
			}

			foreach (SelectionCandidate candidate in candidates)
			{
				yield return candidate.Id.Key;
			}
		}
	}
}
