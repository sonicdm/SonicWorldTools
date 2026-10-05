using BepInEx.Configuration;

namespace SonicWorldTools.Config
{
	internal static class PluginConfig
	{
		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> DebugLogging;

		internal static void Bind(ConfigFile config)
		{
			Enabled = config.Bind("General", "Enabled", true, "Enable SonicWorldTools.");
			DebugLogging = config.Bind(
				"General",
				"DebugLogging",
				false,
				"Verbose diagnostic logging to BepInEx LogOutput.log.");
		}
	}
}
