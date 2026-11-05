using System;
using System.IO;
using System.Text;
using BepInEx;
using SonicWorldTools.Config;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Optional Line Tool placement diagnostics.</summary>
	internal static class PieceRepeaterDiagnostics
	{
		private static readonly object FileLock = new object();
		private static string _path;

		internal static void Log(string category, string message)
		{
			if (PluginConfig.LineToolDiagnostics == null || !PluginConfig.LineToolDiagnostics.Value) return;
			try
			{
				lock (FileLock)
				{
					if (_path == null)
					{
						string directory = Path.Combine(Paths.BepInExRootPath, "logs");
						Directory.CreateDirectory(directory);
						_path = Path.Combine(directory, "SonicWorldTools-LineTool.log");
						File.AppendAllText(_path, "\r\n=== Line Tool diagnostics " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\r\n", new UTF8Encoding(false));
					}
					File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + category + "] " + message + "\r\n", new UTF8Encoding(false));
				}
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("[Line Tool] Could not write dedicated diagnostics: " + ex.Message);
			}
		}
	}
}
