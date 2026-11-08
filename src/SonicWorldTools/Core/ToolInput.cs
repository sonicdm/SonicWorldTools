using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Managers;
using SonicWorldTools.Config;
using SonicWorldTools.Modules.PieceRepeater;
using SonicWorldTools.Modules.ScatterProps;

namespace SonicWorldTools.Core
{
	/// <summary>Shared ZInput/Unity input polling and UI-focus guards for owned tools.</summary>
	internal static class ToolInput
	{
		private static int _frame = -1;
		private static readonly Dictionary<KeyCode, bool> KeyDown = new Dictionary<KeyCode, bool>();
		private static readonly Dictionary<KeyCode, bool> KeyHeld = new Dictionary<KeyCode, bool>();
		private static readonly Dictionary<int, bool> MouseDown = new Dictionary<int, bool>();
		private static readonly Dictionary<int, bool> MouseHeld = new Dictionary<int, bool>();
		private static readonly Dictionary<string, bool> ButtonDown = new Dictionary<string, bool>(System.StringComparer.Ordinal);
		private static readonly Dictionary<string, bool> ButtonHeld = new Dictionary<string, bool>(System.StringComparer.Ordinal);
		private static readonly FieldInfo[] ConfiguredKeyFields = GetConfiguredKeyFields();
		private static bool _scrollRead;
		private static float _scrollY;
		private static int _lastMouse2TraceFrame = -1;
		private static int _lastCaptureHeartbeatFrame = -1;
		private static int _lastMouse2DownFrame = -100000;
		private static bool _lastMouse2Held;
		private static bool _mouse2ZInputDown;
		private static bool _mouse2UnityDown;
		private static bool _mouse2ZInputHeld;
		private static bool _mouse2UnityHeld;
		private const string OwnedMouse2ButtonName = "SWT_OwnedToolMouse2";
		private static ButtonConfig _ownedMouse2Button;
		private static bool _ownedMouse2Registered;

		internal static void RegisterOwnedMouse2Button()
		{
			if (_ownedMouse2Registered) return;
			_ownedMouse2Button = new ButtonConfig
				{
					Name = OwnedMouse2ButtonName,
					Key = KeyCode.Mouse2,
					BlockOtherInputs = true
				};
			InputManager.Instance.AddButton(SonicWorldTools.SonicWorldToolsPlugin.PluginGuid, _ownedMouse2Button);
			_ownedMouse2Registered = true;
			SonicWorldTools.SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[ToolInput] Registered Jotunn-owned MMB button with BlockOtherInputs=true.");
		}

		internal static void BeginFrame()
		{
			int frame = Time.frameCount;
			if (_frame == frame) return;
			_frame = frame;
			KeyDown.Clear();
			KeyHeld.Clear();
			MouseDown.Clear();
			MouseHeld.Clear();
			ButtonDown.Clear();
			ButtonHeld.Clear();
			_scrollRead = false;
			_scrollY = 0f;
		}

		/// <summary>
		/// Samples configured bindings before vanilla Player.Update can consume them.
		/// Modules later in the frame read the same cached edges regardless of update order.
		/// </summary>
		internal static void CaptureFrame()
		{
			BeginFrame();
			foreach (FieldInfo field in ConfiguredKeyFields)
			{
				try
				{
					var entry = field.GetValue(null) as ConfigEntry<KeyCode>;
					if (entry == null) continue;
					KeyCode key = entry.Value;
					WasBoundKeyOrMouseDown(key);
					WasBoundKeyOrMouseHeld(key);
				}
				catch (System.Exception ex)
				{
					SonicWorldToolsPlugin.ModLogger?.LogDebug(
						"[ToolInput] Could not prime binding '" + field.Name + "': " + ex.Message);
				}
			}

			WasButtonDown("Attack");
			WasButtonHeld("Attack");
			WasButtonDown("TabLeft");
			WasButtonDown("TabRight");
			TraceMiddleMouseFrame();
			TraceCaptureHeartbeat();
			if (!_scrollRead)
			{
				_scrollRead = true;
				_scrollY = Input.mouseScrollDelta.y;
			}
		}

		internal static bool WasKeyDown(KeyCode key)
		{
			BeginFrame();
			if (key == KeyCode.None) return false;
			if (KeyDown.TryGetValue(key, out bool cached)) return cached;
			bool pressed;
			try
			{
				pressed = ZInput.GetKeyDown(key) || Input.GetKeyDown(key);
			}
			catch
			{
				// Unity input remains a fallback when ZInput is unavailable.
				pressed = Input.GetKeyDown(key);
			}
			KeyDown[key] = pressed;
			return pressed;
		}

		internal static bool WasKeyHeld(KeyCode key)
		{
			BeginFrame();
			if (key == KeyCode.None) return false;
			if (KeyHeld.TryGetValue(key, out bool cached)) return cached;
			bool held;
			try
			{
				held = ZInput.GetKey(key) || Input.GetKey(key);
			}
			catch
			{
				// Unity input remains a fallback when ZInput is unavailable.
				held = Input.GetKey(key);
			}
			KeyHeld[key] = held;
			return held;
		}

		internal static bool WasMouseButtonDown(int button)
		{
			BeginFrame();
			if (MouseDown.TryGetValue(button, out bool cached)) return cached;
			bool pressed;
			try
			{
				bool owned = button == 2 && ShouldClaimMouse2();
				bool zInputPressed = owned
					? ZInput.GetButtonDown(_ownedMouse2Button.Name)
					: ZInput.GetMouseButtonDown(button);
				bool unityPressed = Input.GetMouseButtonDown(button);
				pressed = zInputPressed || (!owned && unityPressed);
				if (button == 2)
				{
					_mouse2ZInputDown = zInputPressed;
					_mouse2UnityDown = unityPressed;
				}
			}
			catch
			{
				// Fall through to Unity input.
				pressed = Input.GetMouseButtonDown(button);
				if (button == 2)
				{
					_mouse2ZInputDown = false;
					_mouse2UnityDown = pressed;
				}
			}
			MouseDown[button] = pressed;
			if (button == 2 && pressed) _lastMouse2DownFrame = Time.frameCount;
			return pressed;
		}

		internal static bool WasMouseButtonPressedRecently(int button, int maxFrames)
		{
			BeginFrame();
			return button == 2 && Time.frameCount - _lastMouse2DownFrame <= maxFrames;
		}

		internal static bool WasMouseButtonHeld(int button)
		{
			BeginFrame();
			if (MouseHeld.TryGetValue(button, out bool cached)) return cached;
			bool held;
			try
			{
				bool owned = button == 2 && ShouldClaimMouse2();
				bool zInputHeld = owned
					? ZInput.GetButton(_ownedMouse2Button.Name)
					: ZInput.GetMouseButton(button);
				bool unityHeld = Input.GetMouseButton(button);
				held = zInputHeld || (!owned && unityHeld);
				if (button == 2)
				{
					_mouse2ZInputHeld = zInputHeld;
					_mouse2UnityHeld = unityHeld;
				}
			}
			catch
			{
				// Fall through to Unity input.
				held = Input.GetMouseButton(button);
				if (button == 2)
				{
					_mouse2ZInputHeld = false;
					_mouse2UnityHeld = held;
				}
			}
			MouseHeld[button] = held;
			return held;
		}

		private static bool ShouldClaimMouse2()
		{
			if (!_ownedMouse2Registered || !Player.m_localPlayer || !ToolRuntime.InBuildMode
			    || !ToolRuntime.InfinityHammerEquipped || ToolRuntime.TextInputBlocked
			    || !ToolRuntime.TryGetSelectedToolName(out string selectedTool)) return false;
			return OwnedToolRemoveButtonInputPatch.OwnsMiddleMouse(selectedTool);
		}

		private static void TraceMiddleMouseFrame()
		{
			if (_lastMouse2TraceFrame == Time.frameCount) return;
			_lastMouse2TraceFrame = Time.frameCount;
			bool down = WasMouseButtonDown(2);
			bool held = WasMouseButtonHeld(2);
			if (!down && held == _lastMouse2Held) return;

			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[InputTrace] frame=" + Time.frameCount
				+ " MMB down=" + down
				+ " held=" + held
				+ " sources(zDown=" + _mouse2ZInputDown
				+ ",unityDown=" + _mouse2UnityDown
				+ ",zHeld=" + _mouse2ZInputHeld
				+ ",unityHeld=" + _mouse2UnityHeld + ")"
				+ " buildMode=" + ToolRuntime.InBuildMode
				+ " textBlocked=" + ToolRuntime.TextInputBlocked
				+ " session=" + (ToolSessionCoordinator.Owner != null
					? ToolSessionCoordinator.Owner.SessionId + "/" + ToolSessionCoordinator.Owner.IsSessionActive
					: "<none>") + ".");
			_lastMouse2Held = held;
		}

		private static void TraceCaptureHeartbeat()
		{
			int frame = Time.frameCount;
			if (_lastCaptureHeartbeatFrame >= 0 && frame - _lastCaptureHeartbeatFrame < 300) return;
			_lastCaptureHeartbeatFrame = frame;
			SonicWorldToolsPlugin.ModLogger?.LogInfo(
				"[InputTrace] capture heartbeat frame=" + frame
				+ " player=" + (Player.m_localPlayer ? "local" : "none")
				+ " buildMode=" + ToolRuntime.InBuildMode
				+ " textBlocked=" + ToolRuntime.TextInputBlocked
				+ " MMBdown=" + WasMouseButtonDown(2)
				+ " MMBheld=" + WasMouseButtonHeld(2)
				+ " IHtool='" + (ToolRuntime.TryGetSelectedToolName(out string selectedTool) ? selectedTool : "<none>")
				+ "'.");
		}

		internal static bool WasButtonDown(string button)
		{
			BeginFrame();
			if (string.IsNullOrEmpty(button)) return false;
			if (ButtonDown.TryGetValue(button, out bool cached)) return cached;
			bool pressed = false;
			try { pressed = ZInput.GetButtonDown(button); }
			catch { }
			ButtonDown[button] = pressed;
			return pressed;
		}

		internal static bool WasButtonHeld(string button)
		{
			BeginFrame();
			if (string.IsNullOrEmpty(button)) return false;
			if (ButtonHeld.TryGetValue(button, out bool cached)) return cached;
			bool held = false;
			try { held = ZInput.GetButton(button); }
			catch { }
			ButtonHeld[button] = held;
			return held;
		}

		internal static float MouseScrollY
		{
			get
			{
				BeginFrame();
				if (!_scrollRead)
				{
					_scrollRead = true;
					_scrollY = Input.mouseScrollDelta.y;
				}
				return _scrollY;
			}
		}

		internal static bool WasBoundKeyOrMouseDown(KeyCode key)
		{
			if (key == KeyCode.None) return false;
			if (TryGetMouseButton(key, out int button)) return WasMouseButtonDown(button);
			if (WasKeyDown(key)) return true;
			if (key == KeyCode.Equals) return WasKeyDown(KeyCode.KeypadPlus);
			if (key == KeyCode.Minus) return WasKeyDown(KeyCode.KeypadMinus);
			return false;
		}

		internal static bool WasBoundKeyOrMouseHeld(KeyCode key)
		{
			if (key == KeyCode.None) return false;
			if (TryGetMouseButton(key, out int button)) return WasMouseButtonHeld(button);
			if (WasKeyHeld(key)) return true;
			if (key == KeyCode.Equals) return WasKeyHeld(KeyCode.KeypadPlus);
			if (key == KeyCode.Minus) return WasKeyHeld(KeyCode.KeypadMinus);
			return false;
		}

		internal static bool WasModifierHeld(KeyCode configured)
		{
			if (configured == KeyCode.None) return false;
			if (WasBoundKeyOrMouseHeld(configured)) return true;
			if (configured == KeyCode.LeftShift || configured == KeyCode.RightShift)
				return WasKeyHeld(KeyCode.LeftShift) || WasKeyHeld(KeyCode.RightShift);
			if (configured == KeyCode.LeftControl || configured == KeyCode.RightControl)
				return WasKeyHeld(KeyCode.LeftControl) || WasKeyHeld(KeyCode.RightControl);
			if (configured == KeyCode.LeftAlt || configured == KeyCode.RightAlt)
				return WasKeyHeld(KeyCode.LeftAlt) || WasKeyHeld(KeyCode.RightAlt);
			return false;
		}

		internal static bool IsTextInputBlocking()
		{
			try
			{
				if (Console.IsVisible()) return true;
			}
			catch { }

			try
			{
				Chat chat = Chat.instance;
				if (chat && chat.HasFocus()) return true;
			}
			catch { }

			try
			{
				if (TextInput.IsVisible()) return true;
			}
			catch { }

			return false;
		}

		private static bool TryGetMouseButton(KeyCode key, out int button)
		{
			button = key == KeyCode.Mouse0 ? 0
				: key == KeyCode.Mouse1 ? 1
				: key == KeyCode.Mouse2 ? 2
				: key == KeyCode.Mouse3 ? 3
				: key == KeyCode.Mouse4 ? 4
				: key == KeyCode.Mouse5 ? 5
				: key == KeyCode.Mouse6 ? 6
				: -1;
			return button >= 0;
		}

		private static FieldInfo[] GetConfiguredKeyFields()
		{
			FieldInfo[] fields = typeof(PluginConfig).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			var keyFields = new List<FieldInfo>();
			foreach (FieldInfo field in fields)
			{
				if (typeof(ConfigEntry<KeyCode>).IsAssignableFrom(field.FieldType)) keyFields.Add(field);
			}
			return keyFields.ToArray();
		}
	}
}
