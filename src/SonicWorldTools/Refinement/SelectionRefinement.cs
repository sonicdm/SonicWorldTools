using System;
using System.Collections.Generic;

namespace SonicWorldTools.Refinement
{
	/// <summary>
	/// Pure selection refinement: Base ∪ Added − Excluded, keyed by ZDOID string (§7).
	/// </summary>
	public sealed class SelectionRefinement
	{
		private readonly HashSet<string> _base = new HashSet<string>(StringComparer.Ordinal);

		private readonly HashSet<string> _added = new HashSet<string>(StringComparer.Ordinal);

		private readonly HashSet<string> _excluded = new HashSet<string>(StringComparer.Ordinal);

		public IReadOnlyCollection<string> BaseSelection => _base;

		public IReadOnlyCollection<string> AddedSelection => _added;

		public IReadOnlyCollection<string> ExcludedSelection => _excluded;

		/// <summary>Clears base, added, and excluded.</summary>
		public void ClearAll()
		{
			_base.Clear();
			_added.Clear();
			_excluded.Clear();
		}

		/// <summary>Clears manual Added + Excluded only (§7 Reset Refinement).</summary>
		public void ResetRefinement()
		{
			_added.Clear();
			_excluded.Clear();
		}

		/// <summary>Replace base and clear refinements.</summary>
		public void SetBase(IEnumerable<string> keys)
		{
			_base.Clear();
			_added.Clear();
			_excluded.Clear();
			AddKeys(_base, keys);
		}

		/// <summary>Replace base only; leaves Added/Excluded untouched.</summary>
		public void ReplaceBase(IEnumerable<string> keys)
		{
			_base.Clear();
			AddKeys(_base, keys);
		}

		/// <summary>Manual add — may be outside the box; removes from Excluded (§7).</summary>
		public void Add(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return;
			}

			_added.Add(key);
			_excluded.Remove(key);
		}

		/// <summary>Manual exclude — removes from Added (§7).</summary>
		public void Exclude(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return;
			}

			_excluded.Add(key);
			_added.Remove(key);
		}

		public bool ContainsEffective(string key)
		{
			if (string.IsNullOrEmpty(key) || _excluded.Contains(key))
			{
				return false;
			}

			return _base.Contains(key) || _added.Contains(key);
		}

		/// <summary>
		/// Frozen for M5: effective = (Base ∪ Added) − Excluded as ZDOID.ToString() keys.
		/// </summary>
		public HashSet<string> GetEffectiveSelection()
		{
			var effective = new HashSet<string>(_base, StringComparer.Ordinal);
			foreach (string key in _added)
			{
				effective.Add(key);
			}

			foreach (string key in _excluded)
			{
				effective.Remove(key);
			}

			return effective;
		}

		/// <summary>
		/// Remove keys from base/added/excluded (e.g. unloaded before IH hand-off, §21).
		/// </summary>
		public void DropKeys(IEnumerable<string> keys)
		{
			if (keys == null)
			{
				return;
			}

			foreach (string key in keys)
			{
				if (string.IsNullOrEmpty(key))
				{
					continue;
				}

				_base.Remove(key);
				_added.Remove(key);
				_excluded.Remove(key);
			}
		}

		/// <summary>
		/// After a base recompute with persistence on: drop Added/Excluded keys that are no longer valid.
		/// </summary>
		public void PruneRefinementsToValid(IEnumerable<string> validKeys)
		{
			var valid = new HashSet<string>(StringComparer.Ordinal);
			AddKeys(valid, validKeys);
			PruneSet(_added, valid);
			PruneSet(_excluded, valid);
		}

		private static void AddKeys(HashSet<string> target, IEnumerable<string> keys)
		{
			if (keys == null)
			{
				return;
			}

			foreach (string key in keys)
			{
				if (!string.IsNullOrEmpty(key))
				{
					target.Add(key);
				}
			}
		}

		private static void PruneSet(HashSet<string> set, HashSet<string> valid)
		{
			if (set.Count == 0)
			{
				return;
			}

			var remove = new List<string>();
			foreach (string key in set)
			{
				if (!valid.Contains(key))
				{
					remove.Add(key);
				}
			}

			for (int i = 0; i < remove.Count; i++)
			{
				set.Remove(remove[i]);
			}
		}
	}
}
