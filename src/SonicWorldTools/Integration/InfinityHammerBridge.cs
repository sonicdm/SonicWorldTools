using System;
using BepInEx.Bootstrap;

namespace SonicWorldTools.Integration
{
	internal static class InfinityHammerBridge
	{
		private static readonly string[] CandidateGuids =
		{
			"com.github.jerekuusela.InfinityHammer",
			"JereKuusela.InfinityHammer",
			"infinity_hammer",
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
					if (!IsInfinityHammer(id, name))
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
				SonicWorldToolsPlugin.ModLogger?.LogWarning("Infinity Hammer detect failed: " + ex.Message);
			}

			return false;
		}

		private static bool IsInfinityHammer(string pluginGuid, string name)
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

			return string.Equals(name, "Infinity Hammer", StringComparison.OrdinalIgnoreCase);
		}
	}
}
