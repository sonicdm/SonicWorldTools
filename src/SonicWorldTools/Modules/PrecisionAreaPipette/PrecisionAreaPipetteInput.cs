using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using UnityEngine;

namespace SonicWorldTools.Modules.PrecisionAreaPipette
{
	/// <summary>
	/// Configurable corner / cancel / pick-up / reset polling for Precision Area Pipette (§12).
	/// Bindings apply only while the tool is active (controller gates Tick).
	/// </summary>
	internal static class PrecisionAreaPipetteInput
	{
		private const float MaxRayDistance = 80f;

		private static readonly FieldInfo PlaceRayMaskField =
			AccessTools.Field(typeof(Player), "m_placeRayMask");

		private static readonly List<Collider> ColliderBuffer = new List<Collider>(16);

		private static RaycastHit[] HitBuffer = new RaycastHit[64];

		internal readonly struct FrameInput
		{
			internal readonly bool SetCornerDown;

			internal readonly bool CancelDown;

			internal readonly bool PickUpDown;

			internal readonly bool ResetCornersDown;

			internal readonly bool ResetRefinementDown;

			internal readonly bool HasAimPoint;

			internal readonly Vector3 AimPoint;

			internal FrameInput(
				bool setCornerDown,
				bool cancelDown,
				bool pickUpDown,
				bool resetCornersDown,
				bool resetRefinementDown,
				bool hasAimPoint,
				Vector3 aimPoint)
			{
				SetCornerDown = setCornerDown;
				CancelDown = cancelDown;
				PickUpDown = pickUpDown;
				ResetCornersDown = resetCornersDown;
				ResetRefinementDown = resetRefinementDown;
				HasAimPoint = hasAimPoint;
				AimPoint = aimPoint;
			}
		}

		/// <summary>Sample input + hammer-style aim point for this frame.</summary>
		internal static FrameInput Sample()
		{
			if (ToolRuntime.TextInputBlocked)
			{
				return default;
			}

			bool setCorner = WasSetCornerDown();
			bool cancel = WasConfiguredKeyDown(PluginConfig.CancelKey, KeyCode.Escape);
			bool pickUp = WasPickUpDown();
			bool resetCorners = WasConfiguredKeyDown(PluginConfig.ResetCornersKey, KeyCode.Home);
			bool resetRefine = WasConfiguredKeyDown(PluginConfig.ResetRefinementKey, KeyCode.Backspace);
			bool hasAim = TryGetAimPoint(out Vector3 aim);
			return new FrameInput(setCorner, cancel, pickUp, resetCorners, resetRefine, hasAim, aim);
		}

		/// <summary>Raycast / piece hover for a world corner point (build-piece targeting feel).</summary>
		internal static bool TryGetAimPoint(out Vector3 worldPoint)
		{
			worldPoint = Vector3.zero;
			Player player = Player.m_localPlayer;
			if ((Object)(object)player == (Object)null)
			{
				return false;
			}

			if ((Object)(object)GameCamera.instance == (Object)null)
			{
				return false;
			}

			Transform cam = GameCamera.instance.transform;
			Ray ray = new Ray(cam.position, cam.forward);

			// Prefer hovering piece collider hit so Y matches the aimed surface (multi-story).
			Piece hovering = player.GetHoveringPiece();
			if ((Object)(object)hovering != (Object)null
			    && !IsOwnedByLocalPlayer(hovering.gameObject, player))
			{
				ColliderBuffer.Clear();
				hovering.GetComponentsInChildren(ColliderBuffer);
				bool foundPieceHit = false;
				RaycastHit nearestPieceHit = default;
				for (int i = 0; i < ColliderBuffer.Count; i++)
				{
					Collider collider = ColliderBuffer[i];
					if ((Object)(object)collider == (Object)null || !collider.enabled
					    || !collider.Raycast(ray, out RaycastHit pieceHit, MaxRayDistance))
					{
						continue;
					}

					if (!foundPieceHit || pieceHit.distance < nearestPieceHit.distance)
					{
						nearestPieceHit = pieceHit;
						foundPieceHit = true;
					}
				}

				if (foundPieceHit)
				{
					worldPoint = nearestPieceHit.point;
					return true;
				}

				worldPoint = hovering.transform.position;
				return true;
			}

			// Prefer Player.m_placeRayMask (PieceRayTest) so character layers are excluded.
			int mask = Physics.DefaultRaycastLayers;
			if (PlaceRayMaskField != null)
			{
				try
				{
					object raw = PlaceRayMaskField.GetValue(player);
					if (raw is int placeMask && placeMask != 0)
					{
						mask = placeMask;
					}
				}
				catch
				{
					// Keep DefaultRaycastLayers.
				}
			}

			int hitCount = Physics.RaycastNonAlloc(ray, HitBuffer, MaxRayDistance, mask);
			while (hitCount == HitBuffer.Length)
			{
				// Grow only in the rare saturated case so a capped buffer cannot hide a nearer hit.
				HitBuffer = new RaycastHit[HitBuffer.Length * 2];
				hitCount = Physics.RaycastNonAlloc(ray, HitBuffer, MaxRayDistance, mask);
			}

			if (hitCount == 0)
			{
				return false;
			}

			System.Array.Sort(HitBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
			for (int i = 0; i < hitCount; i++)
			{
				RaycastHit hit = HitBuffer[i];
				if ((Object)(object)hit.collider == (Object)null)
				{
					continue;
				}

				if (IsOwnedByLocalPlayer(hit.collider.gameObject, player))
				{
					continue;
				}

				// Mirror PieceRayTest: skip dynamic rigidbodies (dropped items, etc.).
				if ((Object)(object)hit.collider.attachedRigidbody != (Object)null)
				{
					continue;
				}

				worldPoint = hit.point;
				return true;
			}

			return false;
		}

		private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
		{
			internal static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();

			public int Compare(RaycastHit left, RaycastHit right)
			{
				return left.distance.CompareTo(right.distance);
			}
		}

		private static bool IsOwnedByLocalPlayer(GameObject go, Player player)
		{
			if ((Object)(object)go == (Object)null || (Object)(object)player == (Object)null)
			{
				return false;
			}

			Transform t = go.transform;
			Transform root = player.transform;
			while ((Object)(object)t != (Object)null)
			{
				if ((Object)(object)t == (Object)(object)root)
				{
					return true;
				}

				Character character = t.GetComponent<Character>();
				if ((Object)(object)character != (Object)null
				    && (Object)(object)character == (Object)(object)player)
				{
					return true;
				}

				t = t.parent;
			}

			return false;
		}

		internal static bool WasSetCornerDown()
		{
			KeyCode key = PluginConfig.SetCornerKey != null
				? PluginConfig.SetCornerKey.Value
				: KeyCode.Mouse0;

			if (key == KeyCode.None)
			{
				return false;
			}

			// Prefer physical mouse edge (ZInput mouse + Unity) — Attack GetButtonDown is often
			// consumed / ordered after PlayerAttackInput on the first click after Instant tools.
			if (key == KeyCode.Mouse0)
			{
				if (ToolInput.WasMouseButtonDown(0))
				{
					return true;
				}

				// Controller / rebound Attack when not using mouse.
				return WasAttackButtonDown();
			}

			return ToolInput.WasKeyDown(key);
		}

		internal static bool WasRefineModifierHeld()
		{
			KeyCode key = PluginConfig.RefineModifierKey != null
				? PluginConfig.RefineModifierKey.Value
				: KeyCode.None;

			if (key == KeyCode.None)
			{
				return false;
			}

			if (ToolInput.WasKeyHeld(key))
			{
				return true;
			}

			// Convenience: LeftAlt config also accepts RightAlt.
			if (key == KeyCode.LeftAlt)
			{
				return ToolInput.WasKeyHeld(KeyCode.RightAlt);
			}

			if (key == KeyCode.RightAlt)
			{
				return ToolInput.WasKeyHeld(KeyCode.LeftAlt);
			}

			return false;
		}

		internal static bool WasAddPieceDown()
		{
			KeyCode key = PluginConfig.AddPieceKey != null
				? PluginConfig.AddPieceKey.Value
				: KeyCode.Mouse0;

			return ToolInput.WasBoundKeyOrMouseDown(key);
		}

		internal static bool WasExcludePieceDown()
		{
			KeyCode key = PluginConfig.ExcludePieceKey != null
				? PluginConfig.ExcludePieceKey.Value
				: KeyCode.Mouse2;

			return ToolInput.WasBoundKeyOrMouseDown(key);
		}

		private static bool WasConfiguredKeyDown(ConfigEntry<KeyCode> entry, KeyCode fallback)
		{
			KeyCode key = entry != null ? entry.Value : fallback;
			return ToolInput.WasKeyDown(key);
		}

		/// <summary>Ctrl+Delete (no Shift) while Locked — dismantle with refunds.</summary>
		internal static bool WasDismantleSelectionDown()
		{
			KeyCode key = PluginConfig.SelectionRemovalKey != null
				? PluginConfig.SelectionRemovalKey.Value
				: KeyCode.Delete;
			KeyCode mod = PluginConfig.DismantleModifierKey != null
				? PluginConfig.DismantleModifierKey.Value
				: KeyCode.LeftControl;

			if (key == KeyCode.None || mod == KeyCode.None || !ToolInput.WasKeyDown(key))
			{
				return false;
			}

			if (!ToolInput.WasModifierHeld(mod))
			{
				return false;
			}

			// Exclusive: Ctrl+Shift+Delete does not dismantle (prefer permanent delete chord).
			KeyCode delMod = PluginConfig.PermanentDeleteModifierKey != null
				? PluginConfig.PermanentDeleteModifierKey.Value
				: KeyCode.LeftShift;
			if (delMod != KeyCode.None
			    && ToolInput.WasModifierHeld(delMod))
			{
				return false;
			}

			return true;
		}

		/// <summary>Shift+Delete (no Ctrl) while Locked — permanent delete, no drops.</summary>
		internal static bool WasPermanentDeleteSelectionDown()
		{
			KeyCode key = PluginConfig.SelectionRemovalKey != null
				? PluginConfig.SelectionRemovalKey.Value
				: KeyCode.Delete;
			KeyCode mod = PluginConfig.PermanentDeleteModifierKey != null
				? PluginConfig.PermanentDeleteModifierKey.Value
				: KeyCode.LeftShift;

			if (key == KeyCode.None || mod == KeyCode.None || !ToolInput.WasKeyDown(key))
			{
				return false;
			}

			if (!ToolInput.WasModifierHeld(mod))
			{
				return false;
			}

			KeyCode dismantleMod = PluginConfig.DismantleModifierKey != null
				? PluginConfig.DismantleModifierKey.Value
				: KeyCode.LeftControl;
			if (dismantleMod != KeyCode.None
			    && ToolInput.WasModifierHeld(dismantleMod))
			{
				return false;
			}

			return true;
		}

		private static bool WasPickUpDown()
		{
			KeyCode key = PluginConfig.PickUpKey != null
				? PluginConfig.PickUpKey.Value
				: KeyCode.Mouse2;

			if (key == KeyCode.None)
			{
				return false;
			}

			KeyCode mod = PluginConfig.PickUpModifierKey != null
				? PluginConfig.PickUpModifierKey.Value
				: KeyCode.LeftShift;

			if (mod != KeyCode.None && !WasPickUpModifierHeld(mod))
			{
				return false;
			}

			return ToolInput.WasBoundKeyOrMouseDown(key);
		}

		private static bool WasPickUpModifierHeld(KeyCode mod)
		{
			return ToolInput.WasModifierHeld(mod);
		}

		private static bool WasAttackButtonDown()
		{
			try
			{
				if (ToolInput.WasButtonDown("Attack"))
				{
					return true;
				}
			}
			catch
			{
				// ZInput may be unavailable outside play mode.
			}

			return ToolInput.WasMouseButtonDown(0);
		}

		/// <summary>True while LMB / Attack is held (for post-activate release latch).</summary>
		internal static bool IsPrimaryCornerHeld()
		{
			KeyCode key = PluginConfig.SetCornerKey != null
				? PluginConfig.SetCornerKey.Value
				: KeyCode.Mouse0;

			if (key == KeyCode.None)
			{
				return false;
			}

			if (key == KeyCode.Mouse0)
			{
				try
				{
					if (ToolInput.WasButtonHeld("Attack"))
					{
						return true;
					}
				}
				catch
				{
					// Fall through.
				}

				return ToolInput.WasMouseButtonHeld(0);
			}

			return ToolInput.WasKeyHeld(key);
		}
	}
}
