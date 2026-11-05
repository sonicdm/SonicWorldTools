using System;
using System.IO;
using System.Text;
using BepInEx;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Append-only, low-volume diagnostics for reproducing Piece Repeater placement issues.</summary>
	internal static class PieceRepeaterDiagnostics
	{
		private static readonly object FileLock = new object();
		private static string _path;

		internal static void Log(string category, string message)
		{
			try
			{
				lock (FileLock)
				{
					if (_path == null)
					{
						string directory = Path.Combine(Paths.BepInExRootPath, "logs");
						Directory.CreateDirectory(directory);
						_path = Path.Combine(directory, "SonicWorldTools-PieceRepeater.log");
						File.AppendAllText(_path, "\r\n=== Piece Repeater diagnostics " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\r\n", new UTF8Encoding(false));
					}
					File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + category + "] " + message + "\r\n", new UTF8Encoding(false));
				}
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("[PieceRepeater] Could not write dedicated diagnostics: " + ex.Message);
			}
		}
	}
}
