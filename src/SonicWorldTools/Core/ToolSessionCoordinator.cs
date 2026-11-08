using System.Collections.Generic;

namespace SonicWorldTools.Core
{
	/// <summary>
	/// Enforces one active world-input session at a time. Sessions own their
	/// behavior and policy; this coordinator owns only registration and lifetime.
	/// </summary>
	internal static class ToolSessionCoordinator
	{
		private static readonly Dictionary<string, IToolSession> Sessions =
			new Dictionary<string, IToolSession>(System.StringComparer.Ordinal);
		private static IToolSession _owner;

		internal static IToolSession Owner => _owner;

		internal static void Register(IToolSession session)
		{
			if (session == null) throw new System.ArgumentNullException(nameof(session));
			if (string.IsNullOrWhiteSpace(session.SessionId))
				throw new System.ArgumentException("Tool session requires a stable id.", nameof(session));

			if (Sessions.TryGetValue(session.SessionId, out IToolSession existing)
			    && !ReferenceEquals(existing, session))
			{
				if (ReferenceEquals(_owner, existing))
				{
					_owner = null;
					existing.StopSession("session-replaced");
				}
			}
			Sessions[session.SessionId] = session;
		}

		internal static void Unregister(IToolSession session)
		{
			if (session == null) return;
			if (ReferenceEquals(_owner, session))
			{
				_owner = null;
				session.StopSession("session-unregistered");
			}
			if (Sessions.TryGetValue(session.SessionId, out IToolSession registered)
			    && ReferenceEquals(registered, session))
				Sessions.Remove(session.SessionId);
		}

		internal static bool IsOwner(IToolSession session) => ReferenceEquals(_owner, session);

		internal static bool Acquire(IToolSession session)
		{
			if (session == null || !Sessions.TryGetValue(session.SessionId, out IToolSession registered)
			    || !ReferenceEquals(registered, session)) return false;
			if (ReferenceEquals(_owner, session)) return true;

			IToolSession previous = _owner;
			_owner = null;
			if (previous != null) previous.StopSession("replaced-by-" + session.SessionId);

			_owner = session;
			return true;
		}

		internal static void Release(IToolSession session)
		{
			if (ReferenceEquals(_owner, session)) _owner = null;
		}

		internal static bool ShouldSuppress(ToolPlayerAction action, Player player)
		{
			return _owner != null && _owner.IsSessionActive
			       && ToolRuntime.IsCurrentPlayer(player)
			       && _owner.ShouldSuppressPlayerAction(action, player);
		}

		internal static void Reset()
		{
			IToolSession previous = _owner;
			_owner = null;
			previous?.StopSession("runtime-reset");
			Sessions.Clear();
		}
	}
}
