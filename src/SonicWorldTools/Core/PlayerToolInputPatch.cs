using HarmonyLib;
using UnityEngine;

namespace SonicWorldTools.Core
{
	/// <summary>
	/// Samples Line Tool's early settings binding before vanilla Player.Update can
	/// consume the same key for a build action. The controller remains the binding
	/// owner; this patch only gives it the correct execution point.
	/// </summary>
	[HarmonyPatch(typeof(Player), "Update")]
	[HarmonyPriority(Priority.First)]
	internal static class PlayerToolInputPatch
	{
		private static void Prefix(Player __instance)
		{
			ToolRuntime.BeginFrame();
			if (!ToolRuntime.IsCurrentPlayer(__instance)) return;
			ToolInput.CaptureFrame();
			ToolInputRouter.DispatchEarlyPlayerUpdate(__instance);
		}
	}
}
