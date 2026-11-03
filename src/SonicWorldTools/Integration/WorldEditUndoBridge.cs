using System;
using System.Reflection;
using UnityEngine;

namespace SonicWorldTools.Integration
{
	/// <summary>
	/// Soft-dep World Edit Commands <c>UndoHelper</c> so pipette removals can be undone
	/// with IH/WEC <c>undo</c> (default Keypad7). Absent WEC → no-ops.
	/// </summary>
	internal static class WorldEditUndoBridge
	{
		private static bool _resolved;

		private static MethodInfo _beginAction;

		private static MethodInfo _endAction;

		private static MethodInfo _addRemoveAction;

		internal static bool IsAvailable
		{
			get
			{
				EnsureResolved();
				return _beginAction != null && _endAction != null && _addRemoveAction != null;
			}
		}

		internal static void BeginAction()
		{
			EnsureResolved();
			try
			{
				_beginAction?.Invoke(null, null);
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"[SonicWorldTools] UndoHelper.BeginAction failed: " + ex.Message);
			}
		}

		internal static void AddRemoveAction(ZDO zdo)
		{
			if (zdo == null)
			{
				return;
			}

			EnsureResolved();
			try
			{
				_addRemoveAction?.Invoke(null, new object[] { zdo });
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"[SonicWorldTools] UndoHelper.AddRemoveAction failed: " + ex.Message);
			}
		}

		internal static void EndAction()
		{
			EnsureResolved();
			try
			{
				_endAction?.Invoke(null, null);
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"[SonicWorldTools] UndoHelper.EndAction failed: " + ex.Message);
			}
		}

		private static void EnsureResolved()
		{
			if (_resolved)
			{
				return;
			}

			_resolved = true;
			try
			{
				foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					Type helper = asm.GetType("WorldEditCommands.UndoHelper", throwOnError: false)
					             ?? asm.GetType("Service.UndoHelper", throwOnError: false);
					if (helper == null)
					{
						continue;
					}

					_beginAction = helper.GetMethod(
						"BeginAction",
						BindingFlags.Public | BindingFlags.Static,
						null,
						Type.EmptyTypes,
						null);
					_endAction = helper.GetMethod(
						"EndAction",
						BindingFlags.Public | BindingFlags.Static,
						null,
						Type.EmptyTypes,
						null);
					_addRemoveAction = helper.GetMethod(
						"AddRemoveAction",
						BindingFlags.Public | BindingFlags.Static,
						null,
						new[] { typeof(ZDO) },
						null);
					if (_beginAction != null && _endAction != null && _addRemoveAction != null)
					{
						SonicWorldToolsPlugin.ModLogger?.LogInfo(
							"[SonicWorldTools] World Edit undo bridge ready (pipette removals are undoable).");
						return;
					}
				}
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"[SonicWorldTools] UndoHelper resolve failed: " + ex.Message);
			}
		}
	}
}
