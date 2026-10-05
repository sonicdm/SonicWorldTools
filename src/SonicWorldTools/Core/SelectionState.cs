using System.Collections.Generic;

namespace SonicWorldTools.Core
{
	/// <summary>Stable selection id without requiring a live ZDO (M0: string key).</summary>
	public readonly struct SelectionObjectId
	{
		public readonly string Key;

		public SelectionObjectId(string key)
		{
			Key = key ?? string.Empty;
		}

		public override string ToString()
		{
			return Key;
		}
	}

	/// <summary>Base set plus add/exclude refinements.</summary>
	public sealed class SelectionState
	{
		private readonly HashSet<string> _base = new HashSet<string>();

		private readonly HashSet<string> _added = new HashSet<string>();

		private readonly HashSet<string> _excluded = new HashSet<string>();

		public IReadOnlyCollection<string> BaseKeys => _base;

		public IReadOnlyCollection<string> AddedKeys => _added;

		public IReadOnlyCollection<string> ExcludedKeys => _excluded;

		public void Clear()
		{
			_base.Clear();
			_added.Clear();
			_excluded.Clear();
		}

		public void SetBase(IEnumerable<SelectionObjectId> ids)
		{
			_base.Clear();
			_added.Clear();
			_excluded.Clear();
			if (ids == null)
			{
				return;
			}

			foreach (SelectionObjectId id in ids)
			{
				_base.Add(id.Key);
			}
		}

		public void Add(SelectionObjectId id)
		{
			_added.Add(id.Key);
			_excluded.Remove(id.Key);
		}

		public void Exclude(SelectionObjectId id)
		{
			_excluded.Add(id.Key);
			_added.Remove(id.Key);
		}

		public bool ContainsEffective(SelectionObjectId id)
		{
			string key = id.Key;
			if (_excluded.Contains(key))
			{
				return false;
			}

			return _base.Contains(key) || _added.Contains(key);
		}
	}
}
