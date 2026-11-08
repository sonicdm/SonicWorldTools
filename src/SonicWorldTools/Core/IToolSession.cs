namespace SonicWorldTools.Core
{
	/// <summary>
	/// Contract between a world-input tool and the shared runtime. A tool owns its
	/// state machine and policy; the runtime owns exclusivity and lifecycle routing.
	/// </summary>
	internal interface IToolSession
	{
		string SessionId { get; }
		bool IsSessionActive { get; }
		void StopSession(string reason);
		bool ShouldSuppressPlayerAction(ToolPlayerAction action, Player player);
	}

	internal enum ToolPlayerAction
	{
		BuildPlacement,
		Attack,
		Remove,
		Interact,
		PauseMenu
	}
}
