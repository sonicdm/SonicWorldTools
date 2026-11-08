using BepInEx.Configuration;
using System.IO;
using UnityEngine;

namespace SonicWorldTools.Config
{
	internal enum LineToolCurveAlgorithm
	{
		CurrentHermite = 0,
		CentripetalCatmullRom = 1,
	}

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

		internal static ConfigEntry<bool> DryRunSelection;
		internal static ConfigEntry<KeyCode> PieceRepeaterPlaceModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterSampleKey;
		internal static ConfigEntry<KeyCode> PieceRepeaterWaypointKey;
		internal static ConfigEntry<KeyCode> PieceRepeaterOffsetEditorModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterSpacingModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterLevelModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterJointModifier;
		internal static ConfigEntry<KeyCode> PieceRepeaterSettingsHotkey;
		internal static ConfigEntry<float> PieceRepeaterGroundPropSpacing;
		internal static ConfigEntry<bool> PieceRepeaterAnarchyMode;
		internal static ConfigEntry<float> PieceRepeaterGap;
		internal static ConfigEntry<bool> PieceRepeaterRoundedCorners;
		internal static ConfigEntry<float> PieceRepeaterCornerRadius;
		internal static ConfigEntry<LineToolCurveAlgorithm> PieceRepeaterCurveAlgorithm;
		internal static ConfigEntry<bool> PieceRepeaterLevelByDefault;
		internal static ConfigEntry<bool> PieceRepeaterFillDownward;
		internal static ConfigEntry<bool> PieceRepeaterPropFollowCurve;
		internal static ConfigEntry<bool> LineToolDiagnostics;
		internal static ConfigEntry<KeyCode> ScatterSampleKey;
		internal static ConfigEntry<KeyCode> ScatterClearModifier;
		internal static ConfigEntry<int> ScatterMaxSamples;
		internal static ConfigEntry<int> ScatterObjectsPerClick;
		internal static ConfigEntry<float> ScatterInitialRadius;
		internal static ConfigEntry<float> TerrainPathInitialHalfWidth;
		internal static ConfigEntry<float> TerrainPathEdgeSmoothness;
		internal static ConfigEntry<KeyCode> TerrainPathFinishKey;
		internal static ConfigEntry<KeyCode> TerrainPathSmoothKey;
		internal static ConfigEntry<float> LowerTerrainInitialHeight;
		internal static ConfigEntry<float> LowerTerrainSmoothness;
		private static bool _migrateLegacyLineTool;

		internal static void Bind(ConfigFile config)
		{
			_migrateLegacyLineTool = File.Exists(config.ConfigFilePath)
				&& File.ReadAllText(config.ConfigFilePath).Contains("[PieceRepeater]");
			Enabled = config.Bind(
				"General",
				"Enabled",
				true,
				"Enable SonicWorldTools.");

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
				false,
				"Verbose diagnostic logging for selection and integration. Off by default.");

			DryRunSelection = config.Bind(
				"Debug",
				"DryRunSelection",
				false,
				"When true, PickUp logs the effective selection and skips Infinity Hammer hand-off.");

			PieceRepeaterSampleKey = BindLineTool(config, "SampleKey", KeyCode.Mouse2, "Sample a placed build piece while Line Tool is active.");
			PieceRepeaterWaypointKey = BindLineTool(config, "WaypointKey", KeyCode.Mouse0, "Add a waypoint; hold PlaceModifier with this key to place. Mouse0 works best with the hammer's placement interception.");
			PieceRepeaterPlaceModifier = BindLineTool(config, "PlaceModifier", KeyCode.LeftShift, "Hold with WaypointKey to place the batch. Shift avoids the vanilla Ctrl fly-down binding.");
			PieceRepeaterSpacingModifier = BindLineTool(config, "SpacingModifier", KeyCode.LeftAlt, "Hold with the wheel to adjust the gap between pieces.");
			PieceRepeaterLevelModifier = BindLineTool(config, "LevelHeightModifier", KeyCode.LeftAlt, "Hold to keep the path at one height. Alt+wheel still adjusts spacing.");
			PieceRepeaterJointModifier = BindLineTool(config, "WaypointJointModifier", KeyCode.RightAlt, "Hold while clicking a waypoint to align it to a piece joint.");
			PieceRepeaterSettingsHotkey = BindLineTool(config, "SettingsHotkey", KeyCode.F7, "Open Line Tool settings while the Line Tool is active.");
			PieceRepeaterOffsetEditorModifier = BindLineTool(config, "OffsetEditorModifier", KeyCode.LeftShift, "Hold with SettingsHotkey to edit sampled-piece offsets. None disables this shortcut.");
			PieceRepeaterGroundPropSpacing = BindLineTool(config, "GroundPropDefaultSpacing", 2f, "Default spacing in metres for ground props unless overridden by piece YAML.");
			PieceRepeaterAnarchyMode = BindLineTool(config, "AnarchyMode", false, "Allow Line Tool to sample and spawn any registered networked prefab, including trees and creatures. Uses ground-prop spacing and bypasses build-piece placement rules.");
			PieceRepeaterGap = BindLineTool(config, "GapMeters", 0f, "Gap between repeated pieces in metres. Negative values bring pieces closer.");
			PieceRepeaterRoundedCorners = BindLineTool(config, "RoundedCorners", true, "Use curved interpolation through route waypoints; false uses straight segments.");
			PieceRepeaterCornerRadius = BindLineTool(config, "CornerRadius", 4f, "Maximum tangent handle length in metres for curve sampler A (CurrentHermite); ignored by sampler B.");
			PieceRepeaterCurveAlgorithm = BindLineTool(config, "CurveSampler", LineToolCurveAlgorithm.CentripetalCatmullRom, "A/B test: A CurrentHermite; B CentripetalCatmullRom (default, adapts to waypoint spacing and passes through each waypoint). Changes the preview immediately.");
			PieceRepeaterLevelByDefault = BindLineTool(config, "LevelHeightByDefault", false, "Keep all pieces at the first point's height by default.");
			PieceRepeaterFillDownward = BindLineTool(config, "FillDownward", false, "When Maintain Elevation is active, stack each Line Tool placement down to terrain.");
			PieceRepeaterPropFollowCurve = BindLineTool(config, "PropFollowCurve", true, "Align ground props to the route tangent; disable for fixed facing.");
			MigrateOldLineToolModifierDefaults();
			LineToolDiagnostics = config.Bind("LineTool", "WriteDiagnostics", false, "Write detailed Line Tool path and placement diagnostics to BepInEx/logs/SonicWorldTools-LineTool.log. Off by default.");

			ScatterSampleKey = config.Bind("ScatterProps", "SampleKey", KeyCode.Mouse2, "While Scatter Props is selected, sample or remove the aimed networked prop. Mouse2's normal remove action is suppressed by this tool.");
			ScatterClearModifier = config.Bind("ScatterProps", "ClearModifier", KeyCode.LeftShift, "Hold with SampleKey to clear the sample list. Set None to disable this chord; the sonic_scatter clear console command still works.");
			ScatterMaxSamples = config.Bind("ScatterProps", "MaximumSampleTypes", 8, new ConfigDescription("Maximum distinct prop types in the scatter palette (1–32).", new AcceptableValueRange<int>(1, 32)));
			ScatterObjectsPerClick = config.Bind("ScatterProps", "ObjectsPerClick", 20, new ConfigDescription("Total objects spawned per click, shared across sampled types (1–200).", new AcceptableValueRange<int>(1, 200)));
			ScatterInitialRadius = config.Bind("ScatterProps", "InitialRadius", 3f, new ConfigDescription("Initial scatter radius in metres in the Infinity Hammer Cultivator menu. Takes effect when its generated tool YAML is refreshed on game start.", new AcceptableValueRange<float>(0.5f, 30f)));
			TerrainPathInitialHalfWidth = config.Bind("TerrainPaths", "InitialHalfWidth", 2f, new ConfigDescription("Initial path half-width in metres; the full path is twice this wide. The wheel can change it in the Hoe menu.", new AcceptableValueRange<float>(0.25f, 20f)));
			TerrainPathEdgeSmoothness = config.Bind("TerrainPaths", "EdgeSmoothness", 0.5f, new ConfigDescription("WEC edge smoothing strength when Infinity Hammer modifier 1 is held on the final waypoint (0–1).", new AcceptableValueRange<float>(0f, 1f)));
			TerrainPathFinishKey = config.Bind("TerrainPaths", "FinishKey", KeyCode.LeftShift, "Key held on the final waypoint to finish a terrain path. Reflected in the generated Infinity Hammer tool YAML.");
			TerrainPathSmoothKey = config.Bind("TerrainPaths", "SmoothKey", KeyCode.LeftAlt, "Key held on the final waypoint to smooth terrain path edges. Reflected in the generated Infinity Hammer tool YAML.");
			LowerTerrainInitialHeight = config.Bind("LowerTerrain", "InitialHeight", 0.2f, new ConfigDescription("Initial lowering amount in metres in the Hoe menu; the tool height control can change it.", new AcceptableValueRange<float>(0.1f, 10f)));
			LowerTerrainSmoothness = config.Bind("LowerTerrain", "Smoothness", 0.5f, new ConfigDescription("WEC smoothing strength when Infinity Hammer modifier 1 is held while lowering terrain (0–1).", new AcceptableValueRange<float>(0f, 1f)));
		}

		private static void MigrateOldLineToolModifierDefaults()
		{
			// Preserve custom bindings. Only translate the previous stock pair so an existing
			// install gets the same safer defaults as a clean install.
			if (PieceRepeaterPlaceModifier.Value == KeyCode.LeftControl
			    && PieceRepeaterLevelModifier.Value == KeyCode.LeftShift)
			{
				PieceRepeaterPlaceModifier.Value = KeyCode.LeftShift;
				PieceRepeaterLevelModifier.Value = KeyCode.LeftAlt;
			}
		}

		private static ConfigEntry<T> BindLineTool<T>(ConfigFile config, string key, T defaultValue, string description)
		{
			if (_migrateLegacyLineTool)
			{
				var legacy = config.Bind("PieceRepeater", key, defaultValue, description);
				defaultValue = legacy.Value;
				config.Remove(new ConfigDefinition("PieceRepeater", key));
			}
			return config.Bind("LineTool", key, defaultValue, description);
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
					return "Left Alt";
				case KeyCode.RightAlt:
					return "Right Alt";
				case KeyCode.LeftControl:
					return "Ctrl";
				case KeyCode.RightControl:
					return "Right Ctrl";
				case KeyCode.LeftShift:
					return "Shift";
				case KeyCode.RightShift:
					return "Right Shift";
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
