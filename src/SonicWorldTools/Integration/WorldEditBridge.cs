using System;
using BepInEx.Bootstrap;

namespace SonicWorldTools.Integration
{
	internal static class WorldEditBridge
	{
		private static readonly string[] CandidateGuids =
		{
			"com.github.jerekuusela.WorldEditCommands",
			"JereKuusela.WorldEditCommands",
			"world_edit_commands",
		};

		internal static bool TryDetect(out string guid, out string version)
		{
			guid = null;
			version = null;
			try
			{
				foreach (var kv in Chainloader.PluginInfos)
				{
					BepInEx.PluginInfo info = kv.Value;
					if (info?.Metadata == null)
					{
						continue;
					}

					string id = info.Metadata.GUID ?? string.Empty;
					string name = info.Metadata.Name ?? string.Empty;
					if (!IsWorldEditCommands(id, name))
					{
						continue;
					}

					guid = id;
					version = info.Metadata.Version != null ? info.Metadata.Version.ToString() : string.Empty;
					return true;
				}
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("World Edit Commands detect failed: " + ex.Message);
			}

			return false;
		}

		private static bool IsWorldEditCommands(string pluginGuid, string name)
		{
			if (string.IsNullOrEmpty(pluginGuid) && string.IsNullOrEmpty(name))
			{
				return false;
			}

			foreach (string c in CandidateGuids)
			{
				if (string.Equals(pluginGuid, c, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return string.Equals(name, "World Edit Commands", StringComparison.OrdinalIgnoreCase)
				|| name.IndexOf("World Edit", StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}
