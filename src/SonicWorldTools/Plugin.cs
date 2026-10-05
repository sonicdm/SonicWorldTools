using BepInEx;
using BepInEx.Logging;
using Jotunn.Utils;
using SonicWorldTools.Config;
using SonicWorldTools.Core;

namespace SonicWorldTools
{
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
	public sealed class SonicWorldToolsPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.sonicdm.valheim.sonicworldtools";

		public const string PluginName = "SonicWorldTools";

		public const string PluginVersion = "0.1.0";

		internal static ManualLogSource ModLogger;

		private void Awake()
		{
			ModLogger = Logger;
			PluginConfig.Bind(Config);
			DependencyDetector.Run();
			ToolInstaller.EnsureInstalled();
			ModLogger.LogInfo("SonicWorldTools 0.1.0 loaded.");
		}

		private void OnDestroy()
		{
			// M0: no Harmony patches. When patches are added, unpatch only this plugin's id (never UnpatchAll).
		}
	}
}
