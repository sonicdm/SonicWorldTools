using System;
using System.Collections.Generic;

using BepInEx;

using BepInEx.Logging;

using HarmonyLib;

using Jotunn.Utils;

using SonicWorldTools.Config;

using SonicWorldTools.Core;

using SonicWorldTools.Install;

using SonicWorldTools.Integration;

using SonicWorldTools.Modules.PrecisionAreaPipette;
using SonicWorldTools.Modules.ScatterProps;
using SonicWorldTools.Modules.PieceRepeater;



namespace SonicWorldTools

{

	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]

	[BepInDependency(Jotunn.Main.ModGuid)]

	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]

	public sealed class SonicWorldToolsPlugin : BaseUnityPlugin

	{

		public const string PluginGuid = "com.sonicdm.valheim.sonicworldtools";



		public const string PluginName = "SonicWorldTools";



		public const string PluginVersion = "0.1.0";



		internal static ManualLogSource ModLogger;



		private static SonicWorldToolsPlugin _instance;



		private PrecisionAreaPipetteController _pipetteController;

		private PieceRepeaterController _pieceRepeaterController;
		private readonly List<Action> _generatedToolUnhooks = new List<Action>();



		private Harmony _harmony;

		private bool _infinityHammerInitialized;

		private float _nextInfinityHammerProbeTime;



		/// <summary>§21: API incompatible — unregister pipette (no destructive fallback).</summary>
		internal static void DisablePipetteDueToIntegrationFailure()
		{
			if (_instance == null)
			{
				return;
			}

			_instance.UnregisterPrecisionAreaPipette();
			ModLogger?.LogWarning("Precision Area Pipette selection is disabled.");
		}



		private void Awake()

		{

			_instance = this;

			ModLogger = Logger;

			PluginConfig.Bind(Config);

			PrefabPlacementMetadata.Load(BepInEx.Paths.ConfigPath, ModLogger);

			// Infinity Hammer may be loaded later in the BepInEx chainloader order.



			_harmony = new Harmony(PluginGuid);

			_harmony.PatchAll();



			HookConfigHotReload();

			// Install the owned YAML before Infinity Hammer initializes its file watcher.
			// This avoids a runtime watcher reload that can duplicate tool entries.
			if (PluginConfig.Enabled != null && PluginConfig.Enabled.Value
			    && PluginConfig.InstallInfinityHammerTools != null
			    && PluginConfig.InstallInfinityHammerTools.Value)
			{
				ToolInstallerBridge.EnsureInstalled();
			}

			TryInitializeInfinityHammerIntegration();



			ModLogger.LogInfo("SonicWorldTools 0.1.0 loaded.");

		}



		private void Update()

		{
			// BepInEx can awaken this plugin before Infinity Hammer has entered PluginInfos.
			// Retry after other plugins finish loading so tool YAML and the pipette register.
			if (!_infinityHammerInitialized && UnityEngine.Time.unscaledTime >= _nextInfinityHammerProbeTime)
			{
				_nextInfinityHammerProbeTime = UnityEngine.Time.unscaledTime + 1f;
				TryInitializeInfinityHammerIntegration();
			}

			// Ownership poll must run even while inactive (ScriptEngine-safe select→arm).
			ToolIdentityTracker.Tick(_pipetteController);
			ScatterPropsCommand.Tick();

			_pieceRepeaterController?.Tick();
			PrefabPlacementMetadata.EnsureCatalog();

			_pipetteController?.Tick();

		}



		private void OnGUI()

		{

			_pipetteController?.DrawHud();


		}



		private void TryInitializeInfinityHammerIntegration()
		{
			if (_infinityHammerInitialized || !InfinityHammerBridge.TryDetect(out _, out _))
			{
				return;
			}

			_infinityHammerInitialized = true;
			DependencyDetector.Run();
			TerrainToolIconOverrides.TryInstall(_harmony);
			ScatterPropsCommand.Register();
			PrefabGeometryDumpCommand.Register();
			PrefabPlacementMetadata.RegisterRefreshCommand();
			_pieceRepeaterController = _pieceRepeaterController ?? new PieceRepeaterController();
			PieceRepeaterCommand.Register();
			PieceRepeaterController.InitializeKeyHints();

			TryRegisterPrecisionAreaPipette();
		}

		private void TryRegisterPrecisionAreaPipette()

		{

			if (!PluginConfig.IsPrecisionAreaPipetteEnabled())

			{

				UnregisterPrecisionAreaPipette();

				ModLogger.LogInfo("Precision Area Pipette disabled by config.");

				return;

			}



			if (!InfinityHammerBridge.TryDetect(out _, out _))

			{

				UnregisterPrecisionAreaPipette();

				ModLogger.LogInfo(

					"Infinity Hammer not detected. Precision Area Pipette disabled.");

				return;

			}



			if (_pipetteController == null)

			{

				_pipetteController = new PrecisionAreaPipetteController();

			}



			PrecisionAreaPipetteCommand.Register();

		}



		private void UnregisterPrecisionAreaPipette()

		{

			if (_pipetteController != null)

			{

				_pipetteController.Deactivate();

			}



			PrecisionAreaPipetteController.ClearInstance();

			_pipetteController = null;

		}



		private void HookConfigHotReload()

		{

			if (PluginConfig.Enabled != null)

			{

				PluginConfig.Enabled.SettingChanged += OnIntegrationSettingChanged;

			}



			if (PluginConfig.InstallInfinityHammerTools != null)

			{

				PluginConfig.InstallInfinityHammerTools.SettingChanged += OnIntegrationSettingChanged;

			}



			if (PluginConfig.PrecisionAreaPipetteEnabled != null)

			{

				PluginConfig.PrecisionAreaPipetteEnabled.SettingChanged += OnPipetteSettingChanged;

			}



			// Preview toggles are read each frame; SettingChanged forces an immediate overlay clear.

			HookPreviewSetting(PluginConfig.ShowBox);

			HookPreviewSetting(PluginConfig.ShowCornerMarkers);

			HookPreviewSetting(PluginConfig.ShowHighlights);

			HookGeneratedToolSetting(PluginConfig.RefineModifierKey);
			HookGeneratedToolSetting(PluginConfig.AddPieceKey);
			HookGeneratedToolSetting(PluginConfig.ExcludePieceKey);
			HookGeneratedToolSetting(PluginConfig.PickUpModifierKey);
			HookGeneratedToolSetting(PluginConfig.PickUpKey);
			HookGeneratedToolSetting(PluginConfig.DismantleModifierKey);
			HookGeneratedToolSetting(PluginConfig.PermanentDeleteModifierKey);
			HookGeneratedToolSetting(PluginConfig.SelectionRemovalKey);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterSampleKey);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterWaypointKey);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterPlaceModifier);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterSpacingModifier);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterLevelModifier);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterJointModifier);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterSettingsHotkey);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterOffsetEditorModifier);
			HookGeneratedToolSetting(PluginConfig.ScatterSampleKey);
			HookGeneratedToolSetting(PluginConfig.ScatterClearModifier);
			HookGeneratedToolSetting(PluginConfig.TerrainPathInitialHalfWidth);
			HookGeneratedToolSetting(PluginConfig.TerrainPathEdgeSmoothness);
			HookGeneratedToolSetting(PluginConfig.LowerTerrainInitialHeight);
			HookGeneratedToolSetting(PluginConfig.LowerTerrainSmoothness);
			HookGeneratedToolSetting(PluginConfig.ScatterInitialRadius);

			// HUD count/dimensions re-read in UpdateHudText each frame — no clear needed.

		}

		private void OnGeneratedToolSettingChanged(object sender, EventArgs e)
		{
			ToolInstallerBridge.EnsureInstalled();
			PieceRepeaterController.RefreshKeyHints();
		}

		private void HookGeneratedToolSetting<T>(BepInEx.Configuration.ConfigEntry<T> entry)
		{
			if (entry == null) return;
			entry.SettingChanged += OnGeneratedToolSettingChanged;
			_generatedToolUnhooks.Add(() => entry.SettingChanged -= OnGeneratedToolSettingChanged);
		}



		private void HookPreviewSetting(BepInEx.Configuration.ConfigEntry<bool> entry)

		{

			if (entry != null)

			{

				entry.SettingChanged += OnPreviewOrHudSettingChanged;

			}

		}



		private void OnIntegrationSettingChanged(object sender, EventArgs e)

		{

			bool enabled = PluginConfig.Enabled != null && PluginConfig.Enabled.Value;

			bool install = PluginConfig.InstallInfinityHammerTools != null

			               && PluginConfig.InstallInfinityHammerTools.Value;



			if (enabled && install)

			{

				ToolInstallerBridge.EnsureInstalled();

			}

			else

			{

				ToolInstallerBridge.OnIntegrationDisabled();

			}



			TryRegisterPrecisionAreaPipette();

		}



		private void OnPipetteSettingChanged(object sender, EventArgs e)

		{

			TryRegisterPrecisionAreaPipette();

		}



		private void OnPreviewOrHudSettingChanged(object sender, EventArgs e)

		{

			// Next Tick/OnGUI will redraw from live config; clear stale overlays immediately.

			PrecisionAreaPipettePreview.Clear();

		}



		private void UnhookPreviewSetting(BepInEx.Configuration.ConfigEntry<bool> entry)

		{

			if (entry != null)

			{

				entry.SettingChanged -= OnPreviewOrHudSettingChanged;

			}

		}



		private void OnDestroy()

		{

			if (_instance == this)

			{

				_instance = null;

			}



			if (PluginConfig.Enabled != null)

			{

				PluginConfig.Enabled.SettingChanged -= OnIntegrationSettingChanged;

			}



			if (PluginConfig.InstallInfinityHammerTools != null)

			{

				PluginConfig.InstallInfinityHammerTools.SettingChanged -= OnIntegrationSettingChanged;

			}



			if (PluginConfig.PrecisionAreaPipetteEnabled != null)

			{

				PluginConfig.PrecisionAreaPipetteEnabled.SettingChanged -= OnPipetteSettingChanged;

			}



			UnhookPreviewSetting(PluginConfig.ShowBox);

			UnhookPreviewSetting(PluginConfig.ShowCornerMarkers);

			UnhookPreviewSetting(PluginConfig.ShowHighlights);
			foreach (Action unhook in _generatedToolUnhooks) unhook();
			_generatedToolUnhooks.Clear();



			UnregisterPrecisionAreaPipette();
			PrefabPlacementMetadata.Shutdown();
			_pieceRepeaterController?.Shutdown();
			_pieceRepeaterController = null;

			if (_harmony != null)

			{

				_harmony.UnpatchSelf();

				_harmony = null;

			}

		}

	}

}
