using SonicWorldTools.Integration;

namespace SonicWorldTools.Core
{
	internal static class DependencyDetector
	{
		internal static void Run()
		{
			bool ih = InfinityHammerBridge.TryDetect(out string ihGuid, out string ihVersion);
			bool wec = WorldEditBridge.TryDetect(out string wecGuid, out string wecVersion);

			if (ih)
			{
				SonicWorldToolsPlugin.ModLogger.LogInfo(
					"Infinity Hammer detected (" + ihGuid + " " + ihVersion + ").");
			}
			else
			{
				SonicWorldToolsPlugin.ModLogger.LogInfo("Infinity Hammer not detected. It is a required dependency; SonicWorldTools tools will not appear.");
			}

			if (wec)
			{
				SonicWorldToolsPlugin.ModLogger.LogInfo(
					"World Edit Commands detected (" + wecGuid + " " + wecVersion + ").");
			}
			else
			{
				SonicWorldToolsPlugin.ModLogger.LogInfo("World Edit Commands not detected. It is a package dependency; undo for Line Tool placements and Precision Select removals will be unavailable.");
			}
		}
	}
}
