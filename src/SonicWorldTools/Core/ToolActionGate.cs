using System;
using System.Collections.Generic;

namespace SonicWorldTools.Core
{
	/// <summary>Declarative boundary between custom tool input and vanilla player actions.</summary>
	internal static class ToolActionGate
	{
		private static readonly Dictionary<ToolPlayerAction, Dictionary<string, Func<Player, bool>>> Claims =
			new Dictionary<ToolPlayerAction, Dictionary<string, Func<Player, bool>>>();
		private static int _trustedPlacementDepth;
		internal static bool IsTrustedPlacement => _trustedPlacementDepth > 0;

		/// <summary>Allows plugin-owned batch placements through the vanilla build-action patches.</summary>
		internal static IDisposable BeginTrustedPlacement()
		{
			_trustedPlacementDepth++;
			return new TrustedPlacementScope();
		}

		private sealed class TrustedPlacementScope : IDisposable
		{
			private bool _disposed;
			public void Dispose()
			{
				if (_disposed) return;
				_disposed = true;
				_trustedPlacementDepth = Math.Max(0, _trustedPlacementDepth - 1);
			}
		}

		internal static void RegisterClaim(ToolPlayerAction action, string owner, Func<Player, bool> claim)
		{
			if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Action claim requires an owner id.", nameof(owner));
			if (claim == null) throw new ArgumentNullException(nameof(claim));
			if (!Claims.TryGetValue(action, out Dictionary<string, Func<Player, bool>> handlers))
				Claims[action] = handlers = new Dictionary<string, Func<Player, bool>>(StringComparer.Ordinal);
			handlers[owner] = claim;
		}

		internal static void UnregisterClaims(string owner)
		{
			foreach (Dictionary<string, Func<Player, bool>> handlers in Claims.Values)
				handlers.Remove(owner);
		}

		internal static void Reset()
		{
			Claims.Clear();
			_trustedPlacementDepth = 0;
		}

		internal static bool ShouldSuppress(ToolPlayerAction action, Player player)
		{
			if (!ToolRuntime.IsCurrentPlayer(player)) return false;
			try
			{
				if (ToolSessionCoordinator.ShouldSuppress(action, player)) return true;
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogError(
					"[ToolActionGate] Session policy failed for " + action + ": " + ex);
			}
			if (Claims.TryGetValue(action, out Dictionary<string, Func<Player, bool>> handlers))
			{
				var snapshot = new List<KeyValuePair<string, Func<Player, bool>>>(handlers);
				foreach (KeyValuePair<string, Func<Player, bool>> entry in snapshot)
				{
					try
					{
						if (entry.Value(player)) return true;
					}
					catch (Exception ex)
					{
						SonicWorldToolsPlugin.ModLogger?.LogError(
							"[ToolActionGate] Claim '" + entry.Key + "' failed for " + action + ": " + ex);
					}
				}
			}
			return false;
		}
	}
}
