using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using UnityEngine;

namespace SonicWorldTools.Integration
{
	/// <summary>
	/// Soft-dep Infinity Hammer detect + M5 hand-off (Path 1 public API).
	/// IH is not referenced at compile time (absent from Reqs/CI); Path 1 members are
	/// invoked through an isolated reflection binder. Not a custom placement engine.
	/// </summary>
	internal static class InfinityHammerBridge
	{
		internal const string SoftFailUnavailableMessage =
			"[SonicWorldTools] Infinity Hammer integration unavailable for this version.";

		internal const string SoftFailNotDetectedMessage =
			"[SonicWorldTools] Infinity Hammer not detected. Precision Area Pipette disabled.";

		private static readonly string[] CandidateGuids =
		{
			"com.github.jerekuusela.InfinityHammer",
			"JereKuusela.InfinityHammer",
			"infinity_hammer",
		};

		private static bool _apiResolved;

		private static bool _apiAvailable;

		private static MethodInfo _hammerHelperInit;

		private static ConstructorInfo _objectSelectionMultiCtor;

		private static MethodInfo _selectionCreateGhost;

		private static MethodInfo _selectionClear;

		private static MethodInfo _selectionGet;

		private static MethodInfo _hammerSelectEmpty;

		private static FieldInfo _playerPlacementGhost;
		private static FieldInfo _playerManualSnapPoint;
		private static MethodInfo _playerFindClosestSnapPoints;

		private static FieldInfo _toolSelectionToolField;

		private static PropertyInfo _toolNameProperty;

		private static FieldInfo _toolNameField;
		private static FieldInfo _configurationEnabledEntry;
		private static PropertyInfo _configurationEnabledValue;
		private static FieldInfo _shapeKeyEntry;
		private static PropertyInfo _shapeKeyValue;

		/// <summary>Temporarily disables IH's global Q-to-shape shortcut while Piece Repeater owns Q.</summary>
		internal static bool TrySuspendShapeShortcut(out IDisposable restoration)
		{
			restoration = null;
			try
			{
				if (!TryDetect(out string guid, out _)
				    || !Chainloader.PluginInfos.TryGetValue(guid, out BepInEx.PluginInfo info)
				    || info?.Instance == null)
				{
					return true;
				}

				// IH declares Configuration as a partial type across its config source files;
				// there is no separate ConfigurationTools runtime type.
				Type configurationType = info.Instance.GetType().Assembly.GetType("InfinityHammer.Configuration", false);
				if (configurationType == null) return false;
				if (_shapeKeyEntry == null || _shapeKeyEntry.DeclaringType != configurationType)
					_shapeKeyEntry = configurationType.GetField("shapeKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				object entry = _shapeKeyEntry?.GetValue(null);
				if (entry == null) return false;
				if (_shapeKeyValue == null || _shapeKeyValue.DeclaringType != entry.GetType())
					_shapeKeyValue = entry.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
				if (_shapeKeyValue == null || !_shapeKeyValue.CanRead || !_shapeKeyValue.CanWrite) return false;

				object previous = _shapeKeyValue.GetValue(entry, null);
				_shapeKeyValue.SetValue(entry, new KeyboardShortcut(KeyCode.None), null);
				restoration = new RestoreInfinityHammerShapeShortcut(entry, previous);
				SonicWorldToolsPlugin.ModLogger?.LogInfo("[PieceRepeater] Suspended IH's Q shape shortcut while the repeater is active.");
				return true;
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("Could not suspend IH shape shortcut while Piece Repeater is active: " + ex.Message);
				return false;
			}
		}

		private sealed class RestoreInfinityHammerShapeShortcut : IDisposable
		{
			private readonly object _entry;
			private readonly object _value;
			private bool _disposed;

			internal RestoreInfinityHammerShapeShortcut(object entry, object value)
			{
				_entry = entry;
				_value = value;
			}

			public void Dispose()
			{
				if (_disposed) return;
				_disposed = true;
				try { _shapeKeyValue?.SetValue(_entry, _value, null); }
				catch (Exception ex)
				{
					SonicWorldToolsPlugin.ModLogger?.LogError("Could not restore IH shape shortcut after Piece Repeater: " + ex.Message);
				}
			}
		}

		/// <summary>
		/// IH's Player.PlacePiece patch substitutes the active ToolSelection's dummy
		/// prefab and dispatches its command. Piece Repeater needs vanilla placement
		/// for each sampled prefab, so briefly disable IH's placement wrapper only
		/// for that synchronous batch and restore the user's setting immediately.
		/// </summary>
		internal static bool TrySuspendPlacementInterception(out IDisposable restoration)
		{
			restoration = null;
			try
			{
				if (!TryDetect(out string guid, out _)
				    || !Chainloader.PluginInfos.TryGetValue(guid, out BepInEx.PluginInfo info)
				    || info?.Instance == null)
				{
					return true;
				}

				Type configurationType = info.Instance.GetType().Assembly.GetType("InfinityHammer.Configuration", false);
				if (configurationType == null) return false;
				if (_configurationEnabledEntry == null || _configurationEnabledEntry.DeclaringType != configurationType)
				{
					_configurationEnabledEntry = configurationType.GetField(
						"configEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				}
				object entry = _configurationEnabledEntry?.GetValue(null);
				if (entry == null) return false;
				if (_configurationEnabledValue == null || _configurationEnabledValue.DeclaringType != entry.GetType())
				{
					_configurationEnabledValue = entry.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
				}
				if (_configurationEnabledValue == null || !_configurationEnabledValue.CanRead || !_configurationEnabledValue.CanWrite)
					return false;

				bool wasEnabled = (bool)_configurationEnabledValue.GetValue(entry, null);
				if (!wasEnabled) return true;
				_configurationEnabledValue.SetValue(entry, false, null);
				restoration = new RestoreInfinityHammerPlacementInterception(entry, wasEnabled);
				return true;
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("Could not suspend IH placement interception for Piece Repeater: " + ex.Message);
				return false;
			}
		}

		private sealed class RestoreInfinityHammerPlacementInterception : IDisposable
		{
			private readonly object _entry;
			private readonly bool _value;
			private bool _disposed;

			internal RestoreInfinityHammerPlacementInterception(object entry, bool value)
			{
				_entry = entry;
				_value = value;
			}

			public void Dispose()
			{
				if (_disposed) return;
				_disposed = true;
				try { _configurationEnabledValue?.SetValue(_entry, _value, null); }
				catch (Exception ex)
				{
					SonicWorldToolsPlugin.ModLogger?.LogError("Could not restore IH placement interception after Piece Repeater batch: " + ex.Message);
				}
			}
		}

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

		internal static string FindConfigDirectory()
		{
			try
			{
				return Paths.ConfigPath;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// When IH arms a continuous <c>ToolSelection</c> (menu Instant often runs
		/// <c>tool &lt;name&gt;</c> before Attack runs the YAML command), return that tool name.
		/// </summary>
		internal static bool TryGetActiveToolSelectionName(out string toolName)
		{
			toolName = null;
			if (!TryDetect(out _, out _) || !TryResolveApi() || _selectionGet == null)
			{
				return false;
			}

			try
			{
				object selection = _selectionGet.Invoke(null, null);
				if (selection == null)
				{
					return false;
				}

				Type selType = selection.GetType();
				if (selType.Name != "ToolSelection")
				{
					return false;
				}

				if (_toolSelectionToolField == null
				    || _toolSelectionToolField.DeclaringType != selType)
				{
					_toolSelectionToolField = selType.GetField(
						"Tool",
						BindingFlags.Public | BindingFlags.Instance);
				}

				if (_toolSelectionToolField == null)
				{
					return false;
				}

				object tool = _toolSelectionToolField.GetValue(selection);
				if (tool == null)
				{
					return false;
				}

				Type toolType = tool.GetType();
				if (_toolNameProperty == null || _toolNameProperty.DeclaringType != toolType)
				{
					_toolNameProperty = toolType.GetProperty(
						"Name",
						BindingFlags.Public | BindingFlags.Instance);
				}

				if (_toolNameProperty != null)
				{
					toolName = _toolNameProperty.GetValue(tool, null) as string;
				}
				else
				{
					if (_toolNameField == null || _toolNameField.DeclaringType != toolType)
					{
						_toolNameField = toolType.GetField(
							"Name",
							BindingFlags.Public | BindingFlags.Instance);
					}

					if (_toolNameField != null)
					{
						toolName = _toolNameField.GetValue(tool) as string;
					}
				}

				return !string.IsNullOrEmpty(toolName);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Capture IH's currently selected build object before an instant tool replaces it.</summary>
		internal static bool TryGetSelectedBuildPrefabId(out string prefabId)
		{
			prefabId = null;
			if (!TryDetect(out _, out _) || !TryResolveApi(retryFailure: true) || _selectionGet == null)
			{
				return false;
			}

			try
			{
				object selection = _selectionGet.Invoke(null, null);
				if (selection == null)
				{
					return false;
				}

				MethodInfo getPiece = selection.GetType().GetMethod(
					"GetSelectedPiece", BindingFlags.Public | BindingFlags.Instance);
				Piece piece = getPiece != null ? getPiece.Invoke(selection, null) as Piece : null;
				if (!piece)
				{
					return false;
				}

				ZNetView view = piece.GetComponent<ZNetView>();
				if (!view)
				{
					return false;
				}

				ZDO zdo = view.GetZDO();
				GameObject registered = zdo != null && ZNetScene.instance
					? ZNetScene.instance.GetPrefab(zdo.GetPrefab())
					: null;
				prefabId = registered ? Utils.GetPrefabName(registered) : Utils.GetPrefabName(view.gameObject);
				return !string.IsNullOrWhiteSpace(prefabId);
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogDebug("Could not capture IH selected prefab: " + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Clear any active Infinity Hammer selection/ghost so Sonic starts without a leftover tool.
		/// Soft no-op when IH or Path 1 API is unavailable.
		/// </summary>
		internal static bool TryClearActiveSelection()
		{
			if (!TryDetect(out _, out _) || !TryResolveApi() || _selectionClear == null)
			{
				return false;
			}

			try
			{
				_selectionClear.Invoke(null, null);
				return true;
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Infinity Hammer Selection.Clear failed: " + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// True when the local player has a placement ghost (another Hammer/IH tool took over).
		/// Uses reflection — <c>Player.m_placementGhost</c> is not public in stripped refs.
		/// </summary>
		internal static bool HasActivePlacementGhost(Player player)
		{
			return TryGetPlacementGhost(player, out GameObject ghost) && ghost;
		}

		internal static bool TryGetPlacementGhost(Player player, out GameObject ghost)
		{
			ghost = null;
			if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
			{
				return false;
			}

			try
			{
				if (_playerPlacementGhost == null)
				{
					_playerPlacementGhost = typeof(Player).GetField(
						"m_placementGhost",
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}

				if (_playerPlacementGhost == null)
				{
					return false;
				}

				ghost = _playerPlacementGhost.GetValue(player) as GameObject;
				return (UnityEngine.Object)(object)ghost != (UnityEngine.Object)null;
			}
			catch
			{
				ghost = null;
				return false;
			}
		}

		internal static bool TryGetBuildSurface(Player player, out Vector3 point, out Vector3 normal, out bool terrain)
		{
			point = Vector3.zero;
			normal = Vector3.up;
			terrain = false;
			if (!player) return false;
			MethodInfo rayTest = typeof(Player).GetMethod("PieceRayTest", BindingFlags.Instance | BindingFlags.NonPublic);
			if (rayTest == null) return false;
			object[] args = { null, null, null, null, null, false };
			if (!(bool)rayTest.Invoke(player, args)) return false;
			point = (Vector3)args[0];
			terrain = args[3] is Heightmap heightmap && heightmap;
			normal = terrain ? Vector3.up : (Vector3)args[1];
			return true;
		}

		internal static bool TryFindSnapPair(Player player, Transform ghostTransform, int manualSnapPoint,
			out Transform sourceSnapPoint, out Transform targetSnapPoint)
		{
			sourceSnapPoint = null;
			targetSnapPoint = null;
			if (!player || !ghostTransform) return false;
			try
			{
				Type playerType = typeof(Player);
				if (_playerManualSnapPoint == null || _playerManualSnapPoint.DeclaringType != playerType)
					_playerManualSnapPoint = playerType.GetField("m_manualSnapPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (_playerFindClosestSnapPoints == null || _playerFindClosestSnapPoints.DeclaringType != playerType)
					_playerFindClosestSnapPoints = playerType.GetMethod("FindClosestSnapPoints", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (_playerManualSnapPoint == null || _playerFindClosestSnapPoints == null) return false;

				object previousIndex = _playerManualSnapPoint.GetValue(player);
				try
				{
					_playerManualSnapPoint.SetValue(player, manualSnapPoint);
					object[] args = { ghostTransform, 0.5f, null, null, new List<Piece>() };
					if (!(bool)_playerFindClosestSnapPoints.Invoke(player, args)) return false;
					Transform first = args[2] as Transform;
					Transform second = args[3] as Transform;
					if (first && first.IsChildOf(ghostTransform))
					{
						sourceSnapPoint = first;
						targetSnapPoint = second;
					}
					else if (second && second.IsChildOf(ghostTransform))
					{
						sourceSnapPoint = second;
						targetSnapPoint = first;
					}
					return sourceSnapPoint && targetSnapPoint;
				}
				finally
				{
					_playerManualSnapPoint.SetValue(player, previousIndex);
				}
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogDebug("[PieceRepeater] Native snap search unavailable: " + ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Path 1 hand-off: resolve effective ZDOID keys → live <see cref="ZNetView"/>s,
		/// create IH multi-object selection ghost (copy), then caller deactivates Sonic tool.
		/// </summary>
		/// <returns>
		/// True when IH ghost was created. False on soft-fail (missing IH, API drift, empty set).
		/// When <paramref name="disablePipette"/> is true, caller should disable pipette selection (§21).
		/// </returns>
		internal static bool TryHandOffCopy(
			IReadOnlyCollection<string> effectiveKeys,
			out List<ZNetView> resolvedViews,
			out List<string> droppedKeys,
			out bool disablePipette,
			out string userMessage)
		{
			resolvedViews = new List<ZNetView>();
			droppedKeys = new List<string>();
			disablePipette = false;
			userMessage = null;

			if (!TryDetect(out _, out _))
			{
				disablePipette = true;
				userMessage = SoftFailNotDetectedMessage;
				SonicWorldToolsPlugin.ModLogger?.LogWarning(SoftFailNotDetectedMessage);
				return false;
			}

			if (!TryResolveApi(retryFailure: true))
			{
				disablePipette = true;
				userMessage = SoftFailUnavailableMessage;
				SonicWorldToolsPlugin.ModLogger?.LogWarning(SoftFailUnavailableMessage);
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Precision Area Pipette selection is disabled.");
				return false;
			}

			ResolveLiveViews(effectiveKeys, resolvedViews, droppedKeys);

			if (resolvedViews.Count == 0)
			{
				userMessage = "[SonicWorldTools] No live objects left to hand off.";
				SonicWorldToolsPlugin.ModLogger?.LogWarning(userMessage);
				return false;
			}

			try
			{
				_hammerHelperInit.Invoke(null, null);

				object[] ctorArgs = BuildMultiCtorArgs(resolvedViews);
				object selection = _objectSelectionMultiCtor.Invoke(ctorArgs);
				if (selection == null)
				{
					disablePipette = true;
					userMessage = SoftFailUnavailableMessage;
					SonicWorldToolsPlugin.ModLogger?.LogWarning(SoftFailUnavailableMessage);
					return false;
				}

				object ghost = _selectionCreateGhost.Invoke(null, new[] { selection });
				var ghostGo = ghost as GameObject;
				if ((UnityEngine.Object)(object)ghostGo == (UnityEngine.Object)null)
				{
					// A missing placement ghost is a runtime state failure (for example, IH
					// is not currently in placement mode), not evidence that its API drifted.
					// Keep the pipette registered so the player can restore Hammer placement
					// mode and retry. Do not clear the selected tool on this failure path.
					userMessage = "[SonicWorldTools] Infinity Hammer did not create a placement ghost. Restore Hammer placement mode and retry.";
					SonicWorldToolsPlugin.ModLogger?.LogWarning(userMessage);
					return false;
				}

				_hammerSelectEmpty.Invoke(null, null);

				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[SonicWorldTools] Handed off " + resolvedViews.Count
					+ " object(s) to Infinity Hammer placement ghost"
					+ (droppedKeys.Count > 0 ? " (dropped " + droppedKeys.Count + " unloaded)." : "."));
				return true;
			}
			catch (TargetInvocationException ex)
			{
				Exception inner = ex.InnerException ?? ex;
				disablePipette = true;
				userMessage = SoftFailUnavailableMessage;
				SonicWorldToolsPlugin.ModLogger?.LogWarning(SoftFailUnavailableMessage);
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Precision Area Pipette selection is disabled.");
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Infinity Hammer hand-off failed: " + inner.Message);
				return false;
			}
			catch (Exception ex)
			{
				disablePipette = true;
				userMessage = SoftFailUnavailableMessage;
				SonicWorldToolsPlugin.ModLogger?.LogWarning(SoftFailUnavailableMessage);
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Precision Area Pipette selection is disabled.");
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Infinity Hammer hand-off failed: " + ex.Message);
				return false;
			}
		}

		/// <summary>Resolve ZDOID string keys to live non-Player <see cref="ZNetView"/>s; collect dropped.</summary>
		internal static void ResolveLiveViews(
			IReadOnlyCollection<string> effectiveKeys,
			List<ZNetView> resolvedViews,
			List<string> droppedKeys)
		{
			resolvedViews.Clear();
			droppedKeys.Clear();
			if (effectiveKeys == null || effectiveKeys.Count == 0)
			{
				return;
			}

			ZNetScene scene = ZNetScene.instance;
			if ((UnityEngine.Object)(object)scene == (UnityEngine.Object)null)
			{
				foreach (string key in effectiveKeys)
				{
					droppedKeys.Add(key);
				}

				return;
			}

			foreach (string key in effectiveKeys)
			{
				if (!TryResolveView(scene, key, out ZNetView view))
				{
					droppedKeys.Add(key);
					continue;
				}

				resolvedViews.Add(view);
			}
		}

		internal static bool TryParseZdoid(string key, out ZDOID zdoid)
		{
			zdoid = ZDOID.None;
			if (string.IsNullOrEmpty(key))
			{
				return false;
			}

			int colon = key.IndexOf(':');
			if (colon <= 0 || colon >= key.Length - 1)
			{
				return false;
			}

			if (!long.TryParse(key.Substring(0, colon), out long userId))
			{
				return false;
			}

			if (!uint.TryParse(key.Substring(colon + 1), out uint id))
			{
				return false;
			}

			zdoid = new ZDOID(userId, id);
			return true;
		}

		private static bool TryResolveView(ZNetScene scene, string key, out ZNetView view)
		{
			view = null;
			if (!TryParseZdoid(key, out ZDOID zdoid) || zdoid.IsNone())
			{
				return false;
			}

			GameObject go = scene.FindInstance(zdoid);
			if ((UnityEngine.Object)(object)go == (UnityEngine.Object)null)
			{
				return false;
			}

			view = go.GetComponent<ZNetView>();
			if ((UnityEngine.Object)(object)view == (UnityEngine.Object)null || !view.IsValid())
			{
				view = null;
				return false;
			}

			if ((UnityEngine.Object)(object)view.GetComponent<Player>() != (UnityEngine.Object)null)
			{
				view = null;
				return false;
			}

			ZDO zdo = view.GetZDO();
			if (zdo == null)
			{
				view = null;
				return false;
			}

			return true;
		}

		private static object[] BuildMultiCtorArgs(List<ZNetView> views)
		{
			ParameterInfo[] parameters = _objectSelectionMultiCtor.GetParameters();
			var args = new object[parameters.Length];
			args[0] = views;
			args[1] = false; // copy (not pick / singleUse)
			for (int i = 2; i < parameters.Length; i++)
			{
				args[i] = null;
			}

			return args;
		}

		private static bool TryResolveApi(bool retryFailure = false)
		{
			if (_apiResolved)
			{
				if (_apiAvailable || !retryFailure)
				{
					return _apiAvailable;
				}

				// A previous lookup may have run before Infinity Hammer finished loading.
				// Retry on an explicit hand-off request instead of permanently caching a miss.
				_apiResolved = false;
			}

			_apiResolved = true;
			_apiAvailable = false;

			try
			{
				Assembly ihAssembly = FindLoadedAssembly("InfinityHammer");
				if (ihAssembly == null)
				{
					SonicWorldToolsPlugin.ModLogger?.LogWarning(
						"Infinity Hammer assembly not loaded.");
					return false;
				}

				Type hammerHelper = ihAssembly.GetType("InfinityHammer.HammerHelper", false);
				Type objectSelection = ihAssembly.GetType("InfinityHammer.ObjectSelection", false);
				Type selection = ihAssembly.GetType("InfinityHammer.Selection", false);
				Type hammer = ihAssembly.GetType("InfinityHammer.Hammer", false);
				Type baseSelection = ihAssembly.GetType("InfinityHammer.BaseSelection", false);

				if (hammerHelper == null || objectSelection == null || selection == null
				    || hammer == null || baseSelection == null)
				{
					SonicWorldToolsPlugin.ModLogger?.LogWarning(
						"Infinity Hammer Path 1 types missing (ObjectSelection/Selection/Hammer).");
					return false;
				}

				_hammerHelperInit = hammerHelper.GetMethod(
					"Init",
					BindingFlags.Public | BindingFlags.Static,
					null,
					Type.EmptyTypes,
					null);
				_selectionCreateGhost = selection.GetMethod(
					"CreateGhost",
					BindingFlags.Public | BindingFlags.Static,
					null,
					new[] { baseSelection },
					null);
				_selectionClear = selection.GetMethod(
					"Clear",
					BindingFlags.Public | BindingFlags.Static,
					null,
					Type.EmptyTypes,
					null);
				_selectionGet = selection.GetMethod(
					"Get",
					BindingFlags.Public | BindingFlags.Static,
					null,
					Type.EmptyTypes,
					null);
				_hammerSelectEmpty = hammer.GetMethod(
					"SelectEmpty",
					BindingFlags.Public | BindingFlags.Static,
					null,
					Type.EmptyTypes,
					null);
				_objectSelectionMultiCtor = FindMultiObjectSelectionCtor(objectSelection);

				if (_hammerHelperInit == null || _selectionCreateGhost == null
				    || _selectionClear == null || _hammerSelectEmpty == null
				    || _objectSelectionMultiCtor == null)
				{
					SonicWorldToolsPlugin.ModLogger?.LogWarning(
						"Infinity Hammer Path 1 members missing (Init/CreateGhost/Clear/SelectEmpty/multi ctor).");
					return false;
				}

				_apiAvailable = true;
				_apiResolved = true;
				return true;
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"Infinity Hammer API resolve failed: " + ex.Message);
				return false;
			}
		}

		private static ConstructorInfo FindMultiObjectSelectionCtor(Type objectSelection)
		{
			ConstructorInfo[] ctors = objectSelection.GetConstructors(
				BindingFlags.Public | BindingFlags.Instance);
			for (int i = 0; i < ctors.Length; i++)
			{
				ParameterInfo[] ps = ctors[i].GetParameters();
				if (ps.Length < 2)
				{
					continue;
				}

				if (ps[1].ParameterType != typeof(bool))
				{
					continue;
				}

				Type first = ps[0].ParameterType;
				if (first == typeof(IEnumerable<ZNetView>)
				    || (first.IsGenericType
				        && first.GetGenericTypeDefinition() == typeof(IEnumerable<>)
				        && first.GetGenericArguments()[0] == typeof(ZNetView)))
				{
					return ctors[i];
				}

				// Some compilers emit IEnumerable`1 with assignable element type.
				if (typeof(IEnumerable<ZNetView>).IsAssignableFrom(first)
				    && first != typeof(ZNetView))
				{
					return ctors[i];
				}
			}

			return null;
		}

		private static Assembly FindLoadedAssembly(string simpleName)
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				Assembly asm = assemblies[i];
				try
				{
					if (string.Equals(asm.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
					{
						return asm;
					}
				}
				catch
				{
					// Dynamic / reflection-only assemblies can throw on GetName.
				}
			}

			return null;
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
