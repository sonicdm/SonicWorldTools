using System;
using System.Collections.Generic;

namespace SonicWorldTools.Core
{
	/// <summary>
	/// Dispatches early player-update input hooks. This is for bindings which must
	/// run before vanilla Player.Update consumes the same input; it does not poll
	/// hardware or own any tool behavior.
	/// </summary>
	internal static class ToolInputRouter
	{
		private static readonly Dictionary<string, Action<Player>> EarlyUpdateHandlers =
			new Dictionary<string, Action<Player>>(StringComparer.Ordinal);

		internal static void RegisterEarlyUpdateHandler(string owner, Action<Player> handler)
		{
			if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Input handler requires an owner id.", nameof(owner));
			if (handler == null) throw new ArgumentNullException(nameof(handler));
			EarlyUpdateHandlers[owner] = handler;
		}

		internal static void Unregister(string owner) => EarlyUpdateHandlers.Remove(owner);

		internal static void DispatchEarlyPlayerUpdate(Player player)
		{
			if (!ToolRuntime.IsCurrentPlayer(player)) return;
			var snapshot = new List<KeyValuePair<string, Action<Player>>>(EarlyUpdateHandlers);
			foreach (KeyValuePair<string, Action<Player>> entry in snapshot)
			{
				try { entry.Value(player); }
				catch (Exception ex)
				{
					SonicWorldToolsPlugin.ModLogger?.LogError(
						"[ToolInputRouter] Early input handler '" + entry.Key + "' failed: " + ex);
				}
			}
		}

		internal static void Reset() => EarlyUpdateHandlers.Clear();
	}
}
