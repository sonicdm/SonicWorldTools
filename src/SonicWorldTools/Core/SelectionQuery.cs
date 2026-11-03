using System;
using System.Collections.Generic;

namespace SonicWorldTools.Core
{
	/// <summary>
	/// Stable selection id for offline tests and adapters. In-game M3+ maps ZDOID → Key
	/// (e.g. ZDOID.ToString()) without storing scene object references.
	/// </summary>
	public readonly struct SelectionObjectId : IEquatable<SelectionObjectId>
	{
		public readonly string Key;

		public SelectionObjectId(string key)
		{
			Key = key ?? string.Empty;
		}

		public bool Equals(SelectionObjectId other)
		{
			return string.Equals(Key, other.Key, StringComparison.Ordinal);
		}

		public override bool Equals(object obj)
		{
			return obj is SelectionObjectId other && Equals(other);
		}

		public override int GetHashCode()
		{
			return Key != null ? StringComparer.Ordinal.GetHashCode(Key) : 0;
		}

		public override string ToString()
		{
			return Key;
		}

		public static bool operator ==(SelectionObjectId left, SelectionObjectId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(SelectionObjectId left, SelectionObjectId right)
		{
			return !left.Equals(right);
		}
	}

	/// <summary>Candidate for a spatially bounded query (caller supplies the nearby set).</summary>
	public readonly struct SelectionCandidate
	{
		public readonly SelectionObjectId Id;

		public readonly float X;

		public readonly float Y;

		public readonly float Z;

		public SelectionCandidate(SelectionObjectId id, float x, float y, float z)
		{
			Id = id;
			X = x;
			Y = y;
			Z = z;
		}
	}

	/// <summary>
	/// Origin-mode inclusion: object selected iff canonical position ∈ normalized volume.
	/// Caller must pass a spatially bounded candidate set — never a full-world scan (§19).
	/// </summary>
	public static class SelectionQuery
	{
		public static IReadOnlyCollection<SelectionObjectId> Query(
			SelectionVolume volume,
			IEnumerable<SelectionCandidate> candidates)
		{
			var result = new List<SelectionObjectId>();
			if (candidates == null)
			{
				return result;
			}

			foreach (SelectionCandidate candidate in candidates)
			{
				if (volume.Contains(candidate.X, candidate.Y, candidate.Z))
				{
					result.Add(candidate.Id);
				}
			}

			return result;
		}
	}
}
