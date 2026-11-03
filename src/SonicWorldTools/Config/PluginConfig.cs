using BepInEx.Configuration;
using UnityEngine;

namespace SonicWorldTools.Config
{
	/// <summary>MVP inclusion rule. Collider is reserved and falls back to Origin.</summary>
	internal enum SelectionIntersectionMode
	{
		Origin = 0,
		Collider = 1,
	}

	/// <summary>BepInEx binds for SonicWorldTools (§12–§14).</summary>
	internal static class PluginConfig
	{
		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> PrecisionAreaPipetteEnabled;

		/// <summary>Config key SelectionMode; only Origin is implemented (M2).</summary>
		internal static ConfigEntry<SelectionIntersectionMode> SelectionMode;

		internal static ConfigEntry<bool> ShowDimensions;

		internal static ConfigEntry<bool> ShowSelectedCount;

		/// <summary>When true, keep valid Added/Excluded across corner resize (§7 / §13).</summary>
		internal static ConfigEntry<bool> PersistRefinementsAfterResize;

		/// <summary>Uniform margin applied to the selection volume and its preview.</summary>
		internal static ConfigEntry<float> SelectionPadding;

		internal static ConfigEntry<KeyCode> SetCornerKey;

		/// <summary>Optional chord for add/exclude. Default None — dedicated keys alone.</summary>
		internal static ConfigEntry<KeyCode> RefineModifierKey;

		internal static ConfigEntry<KeyCode> AddPieceKey;

		internal static ConfigEntry<KeyCode> ExcludePieceKey;

		internal static ConfigEntry<KeyCode> CancelKey;

		/// <summary>Hold with <see cref="PickUpKey"/>. Default LeftShift (IH copy chord).</summary>
		internal static ConfigEntry<KeyCode> PickUpModifierKey;

		internal static ConfigEntry<KeyCode> PickUpKey;

		internal static ConfigEntry<KeyCode> ResetCornersKey;

		internal static ConfigEntry<KeyCode> ResetRefinementKey;

		/// <summary>Shared key for Ctrl+DEL dismantle and Shift+DEL permanent delete.</summary>
		internal static ConfigEntry<KeyCode> SelectionRemovalKey;

		internal static ConfigEntry<KeyCode> DismantleModifierKey;

		internal static ConfigEntry<KeyCode> PermanentDeleteModifierKey;

		internal static ConfigEntry<bool> ShowBox;

		internal static ConfigEntry<bool> ShowCornerMarkers;

		internal static ConfigEntry<bool> ShowHighlights;

		internal static ConfigEntry<bool> InstallInfinityHammerTools;

		internal static ConfigEntry<bool> VerboseLogging;

		internal static ConfigEntry<bool> DroppedDefaultPrefabPlacementYaml;

		internal static ConfigEntry<bool> DryRunSelection;
		internal static ConfigEntry<KeyCode> PieceRepeaterPlaceModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterSpacingModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterLevelModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterJointModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterSettingsHotkey;
		internal static ConfigEntry<float> PieceRepeaterGroundPropSpacing;
		internal static ConfigEntry<float> PieceRepeaterGap;
		internal static ConfigEntry<bool> PieceRepeaterRoundedCorners;
		internal static ConfigEntry<float> PieceRepeaterCornerRadius;
		internal static ConfigEntry<bool> PieceRepeaterLevelByDefault;
		internal static ConfigEntry<bool> PieceRepeaterPropFollowCurve;

		internal static void Bind(ConfigFile config)
		{
			Enabled = config.Bind(
				"General",
				"Enabled",
				true,
				"Enable SonicWorldTools.");

			DroppedDefaultPrefabPlacementYaml = config.Bind(
				"PrefabPlacement",
				"DroppedDefaultYml",
				false,
				"Tracks whether the default prefab placement YAML has been created in the config folder.");

			PrecisionAreaPipetteEnabled = config.Bind(
				"PrecisionAreaPipette",
				"Enabled",
				true,
				"Enable Precision Area Pipette when Infinity Hammer is present.");

			SelectionMode = config.Bind(
				"PrecisionAreaPipette",
				"SelectionMode",
				SelectionIntersectionMode.Origin,
				"Object inclusion: Origin = canonical world position inside the volume. Collider is not implemented and falls back to Origin.");

			ShowDimensions = config.Bind(
				"PrecisionAreaPipette",
				"ShowDimensions",
				true,
				"Show W × D × H on the precision pipette HUD (e.g. 7.0m × 10.0m × 3.0m). Applies immediately.");

			ShowSelectedCount = config.Bind(
				"PrecisionAreaPipette",
				"ShowSelectedCount",
				true,
				"Show selected object count on the precision pipette HUD. Applies immediately.");

			PersistRefinementsAfterResize = config.Bind(
				"PrecisionAreaPipette",
				"PersistRefinementsAfterResize",
				true,
				"Keep manual add/exclude refinements when Corner A/B change, for ZDOIDs that remain valid.");

			SelectionPadding = config.Bind(
				"PrecisionAreaPipette",
				"SelectionPadding",
				0.1f,
				"Expand the selected volume and its preview by this many meters on every face. Set to 0 for exact corners; values are clamped to 0–2 m.");

			const string bindNote =
				" Applies only while Precision Area Pipette is active. "
				+ "Infinity Hammer / vanilla Hammer may also see Attack/clicks; DragNBuild can intercept build clicks — "
				+ "disable DragNBuild or rebind here if corners feel stolen. Right-click is not bound (Hammer menu preserved).";

			SetCornerKey = config.Bind(
				"PrecisionAreaPipette",
				"SetCornerKey",
				KeyCode.Mouse0,
				"Set / confirm Corner A/B/C (and resize from Locked)." + bindNote
				+ " When Mouse0, the vanilla Attack button is also accepted. Not used for refine.");

			RefineModifierKey = config.Bind(
				"PrecisionAreaPipette",
				"RefineModifierKey",
				KeyCode.LeftAlt,
				"Hold with AddPieceKey / ExcludePieceKey while Locked. Default LeftAlt."
				+ bindNote
				+ " LeftAlt also accepts RightAlt. Set None for Add/Exclude keys alone.");

			AddPieceKey = config.Bind(
				"PrecisionAreaPipette",
				"AddPieceKey",
				KeyCode.Mouse0,
				"While Locked + RefineModifierKey: add the hovered piece (including outside the box)."
				+ bindNote
				+ " Default Alt+LMB.");

			ExcludePieceKey = config.Bind(
				"PrecisionAreaPipette",
				"ExcludePieceKey",
				KeyCode.Mouse2,
				"While Locked + RefineModifierKey: exclude the hovered piece."
				+ bindNote
				+ " Default Alt+MMB (plain MMB Remove stays suppressed while pipette is active).");

			CancelKey = config.Bind(
				"PrecisionAreaPipette",
				"CancelKey",
				KeyCode.Escape,
				"Cancel the current precision selection (clear corners, refinement, and preview; stay armed)." + bindNote);

			PickUpModifierKey = config.Bind(
				"PrecisionAreaPipette",
				"PickUpModifierKey",
				KeyCode.LeftShift,
				"Hold with PickUpKey to hand off the selection. Default LeftShift (matches IH Copy)."
				+ bindNote
				+ " LeftShift also accepts RightShift. Set None for PickUpKey alone.");

			PickUpKey = config.Bind(
				"PrecisionAreaPipette",
				"PickUpKey",
				KeyCode.Mouse2,
				"With PickUpModifierKey: hand the locked selection to Infinity Hammer as a copy ghost."
				+ bindNote
				+ " Default Mouse2 (MMB) — same chord as IH sample/copy (Shift+Mouse3).");

			ResetCornersKey = config.Bind(
				"PrecisionAreaPipette",
				"ResetCornersKey",
				KeyCode.Home,
				"Clear corners and selection volume; return to Corner A. Same clear as Cancel." + bindNote);

			ResetRefinementKey = config.Bind(
				"PrecisionAreaPipette",
				"ResetRefinementKey",
				KeyCode.Backspace,
				"While Locked: clear manual add/exclude only; keep corners and base volume." + bindNote);

			SelectionRemovalKey = config.Bind(
				"PrecisionAreaPipette",
				"SelectionRemovalKey",
				KeyCode.Delete,
				"While Locked: chord key for selection dismantle / permanent delete." + bindNote
				+ " Plain Delete (no modifier) is left for Dismantleheim ActivateKey.");

			DismantleModifierKey = config.Bind(
				"PrecisionAreaPipette",
				"DismantleModifierKey",
				KeyCode.LeftControl,
				"Hold with SelectionRemovalKey to dismantle the locked selection (material refunds)."
				+ bindNote
				+ " Default Ctrl+Delete. LeftControl also accepts RightControl. Requires no Shift.");

			PermanentDeleteModifierKey = config.Bind(
				"PrecisionAreaPipette",
				"PermanentDeleteModifierKey",
				KeyCode.LeftShift,
				"Hold with SelectionRemovalKey to permanently delete the locked selection (no drops)."
				+ bindNote
				+ " Default Shift+Delete. LeftShift also accepts RightShift. Requires no Ctrl.");

			MigrateCollidingPipetteBinds();

			ShowBox = config.Bind(
				"Preview",
				"ShowBox",
				true,
				"Draw the selection wire box. Applies immediately (no restart).");

			ShowCornerMarkers = config.Bind(
				"Preview",
				"ShowCornerMarkers",
				true,
				"Draw Corner A / B / C markers. Applies immediately (no restart).");

			ShowHighlights = config.Bind(
				"Preview",
				"ShowHighlights",
				true,
				"Highlight pieces inside the effective selection. Applies immediately (no restart).");

			InstallInfinityHammerTools = config.Bind(
				"Integration",
				"InstallInfinityHammerTools",
				true,
				"Install/update owned Infinity Hammer tools YAML (infinity_tools_sonicworldtools.yaml) when Infinity Hammer is present.");

			VerboseLogging = config.Bind(
				"Debug",
				"VerboseLogging",
				true,
				"Verbose diagnostic logging (corners, selection counts). Default on during bring-up; set false to quiet.");

			DryRunSelection = config.Bind(
				"Debug",
				"DryRunSelection",
				false,
				"When true, PickUp logs the effective selection and skips Infinity Hammer hand-off.");

			PieceRepeaterPlaceModifier = config.Bind("PieceRepeater", "PlaceModifier", KeyCode.LeftControl, "Hold with click to place the repeated pieces.");
			PieceRepeaterSpacingModifier = config.Bind("PieceRepeater", "SpacingModifier", KeyCode.LeftAlt, "Hold with the mouse wheel to add or remove a gap between repeated pieces.");
			PieceRepeaterLevelModifier = config.Bind("PieceRepeater", "LevelHeightModifier", KeyCode.LeftShift, "Hold to keep the path at one height.");
			PieceRepeaterJointModifier = config.Bind("PieceRepeater", "WaypointJointModifier", KeyCode.RightAlt, "Hold while clicking a waypoint to align it to the nearest piece joint.");
			PieceRepeaterSettingsHotkey = config.Bind("PieceRepeater", "SettingsHotkey", KeyCode.F7, "Open Piece Repeater settings while that tool is active. The key is ignored otherwise.");
			PieceRepeaterGroundPropSpacing = config.Bind("PieceRepeater", "GroundPropDefaultSpacing", 2f, "Default interval in metres for free-standing ground props. Per-piece YAML can override it.");
			PieceRepeaterGap = config.Bind("PieceRepeater", "GapMeters", 0f, "Extra horizontal gap between repeated pieces, in metres.");
			PieceRepeaterRoundedCorners = config.Bind("PieceRepeater", "RoundedCorners", false, "Enable local cubic Bezier interpolation at path turns. Previously completed legs stay fixed.");
			PieceRepeaterCornerRadius = config.Bind("PieceRepeater", "CornerRadius", 1.5f, "Smoothing distance in metres for local Bezier interpolation at each path turn.");
			PieceRepeaterLevelByDefault = config.Bind("PieceRepeater", "LevelHeightByDefault", false, "Keep all pieces at the first point's height without holding the level modifier.");
			PieceRepeaterPropFollowCurve = config.Bind("PieceRepeater", "PropFollowCurve", true, "Align repeated ground props to the path tangent. Disable to keep the first prop's facing.");
		}

		internal static bool IsVerboseLogging()
		{
			return VerboseLogging != null && VerboseLogging.Value;
		}

		internal static bool IsDryRunSelection()
		{
			return DryRunSelection != null && DryRunSelection.Value;
		}

		internal static float GetSelectionPadding()
		{
			float value = SelectionPadding != null ? SelectionPadding.Value : 0.1f;
			return (float)System.Math.Max(0.0, System.Math.Min(2.0, value));
		}

		/// <summary>Returns Origin always for MVP; warns if Collider is requested.</summary>
		internal static SelectionIntersectionMode GetEffectiveSelectionMode()
		{
			SelectionIntersectionMode mode = SelectionMode != null
				? SelectionMode.Value
				: SelectionIntersectionMode.Origin;

			if (mode == SelectionIntersectionMode.Collider)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning(
					"SelectionMode=Collider is not implemented; using Origin.");
				return SelectionIntersectionMode.Origin;
			}

			return SelectionIntersectionMode.Origin;
		}

		internal static bool IsPrecisionAreaPipetteEnabled()
		{
			return (Enabled == null || Enabled.Value)
			       && (PrecisionAreaPipetteEnabled == null || PrecisionAreaPipetteEnabled.Value);
		}

		/// <summary>
		/// One-shot: rewrite saved binds that collide with IH Place/Remove, chat, or Use.
		/// Existing cfg keeps old values otherwise — without this, Alt+MMB/Enter stay sticky.
		/// </summary>
		private static void MigrateCollidingPipetteBinds()
		{
			bool changed = false;

			// Restore Alt+mouse refine if a prior migrate forced = / - / no modifier.
			if (AddPieceKey != null && AddPieceKey.Value == KeyCode.Equals)
			{
				AddPieceKey.Value = KeyCode.Mouse0;
				changed = true;
			}

			if (ExcludePieceKey != null && ExcludePieceKey.Value == KeyCode.Minus)
			{
				ExcludePieceKey.Value = KeyCode.Mouse2;
				changed = true;
			}

			if (RefineModifierKey != null && RefineModifierKey.Value == KeyCode.None
			    && AddPieceKey != null && AddPieceKey.Value == KeyCode.Mouse0)
			{
				RefineModifierKey.Value = KeyCode.LeftAlt;
				changed = true;
			}

			if (PickUpKey != null
			    && (PickUpKey.Value == KeyCode.Return
			        || PickUpKey.Value == KeyCode.KeypadEnter
			        || PickUpKey.Value == KeyCode.P))
			{
				PickUpKey.Value = KeyCode.Mouse2;
				changed = true;
			}

			if (PickUpModifierKey != null
			    && PickUpModifierKey.Value == KeyCode.None
			    && PickUpKey != null
			    && PickUpKey.Value == KeyCode.Mouse2)
			{
				PickUpModifierKey.Value = KeyCode.LeftShift;
				changed = true;
			}

			if (ResetRefinementKey != null && ResetRefinementKey.Value == KeyCode.X)
			{
				ResetRefinementKey.Value = KeyCode.Backspace;
				changed = true;
			}

			if (ResetCornersKey != null && ResetCornersKey.Value == KeyCode.R)
			{
				ResetCornersKey.Value = KeyCode.Home;
				changed = true;
			}

			if (changed)
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo(
					"[SonicWorldTools] Migrated Precision Area Pipette binds "
					+ "(Alt+LMB add, Alt+MMB exclude, Shift+MMB pick up, Home reset corners).");
			}
		}

		/// <summary>HUD label for pick-up chord (e.g. Shift+MMB).</summary>
		internal static string FormatPickUpChordLabel()
		{
			KeyCode mod = PickUpModifierKey != null ? PickUpModifierKey.Value : KeyCode.LeftShift;
			KeyCode key = PickUpKey != null ? PickUpKey.Value : KeyCode.Mouse2;
			if (mod == KeyCode.None)
			{
				return FormatKeyLabel(key);
			}

			return FormatKeyLabel(mod) + "+" + FormatKeyLabel(key);
		}

		/// <summary>Short HUD label for a configured key.</summary>
		internal static string FormatKeyLabel(KeyCode key)
		{
			switch (key)
			{
				case KeyCode.Mouse0:
					return "LMB";
				case KeyCode.Mouse1:
					return "RMB";
				case KeyCode.Mouse2:
					return "MMB";
				case KeyCode.LeftAlt:
				case KeyCode.RightAlt:
					return "Alt";
				case KeyCode.LeftControl:
				case KeyCode.RightControl:
					return "Ctrl";
				case KeyCode.LeftShift:
				case KeyCode.RightShift:
					return "Shift";
				case KeyCode.Return:
				case KeyCode.KeypadEnter:
					return "Enter";
				case KeyCode.Escape:
					return "Esc";
				case KeyCode.Equals:
				case KeyCode.KeypadPlus:
					return "=";
				case KeyCode.Minus:
				case KeyCode.KeypadMinus:
					return "-";
				case KeyCode.Backspace:
					return "Backspace";
				case KeyCode.Home:
					return "Home";
				case KeyCode.Delete:
					return "Del";
				case KeyCode.None:
					return "None";
				default:
					return key.ToString();
			}
		}
	}
}
