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

	[BepInDependency(InfinityHammerGuid)]

	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]

	public sealed class SonicWorldToolsPlugin : BaseUnityPlugin

	{

		public const string PluginGuid = "com.sonicdm.valheim.sonicworldtools";



		public const string PluginName = "SonicWorldTools";

		/// <summary>Infinity Hammer is a hard dependency; BepInEx loads it before this plugin.</summary>
		public const string InfinityHammerGuid = "infinity_hammer";



		public const string PluginVersion = "0.1.1";



		internal static ManualLogSource ModLogger;



		private static SonicWorldToolsPlugin _instance;



		private PrecisionAreaPipetteController _pipetteController;

		private PieceRepeaterController _pieceRepeaterController;
		private readonly List<Action> _generatedToolUnhooks = new List<Action>();



		private Harmony _harmony;

		private bool _infinityHammerInitialized;

		private bool _infinityHammerRetried;



		private void Awake()

		{

			_instance = this;

			ModLogger = Logger;

			PluginConfig.Bind(Config);
			ToolInput.RegisterOwnedMouse2Button();

			PrefabPlacementMetadata.Load(BepInEx.Paths.ConfigPath, ModLogger);




			_harmony = new Harmony(PluginGuid);

			_harmony.PatchAll();
			ToolActionGate.RegisterClaim(ToolPlayerAction.Remove, "line-tool-sample", PieceRepeaterController.OwnsMiddleClick);
			ToolActionGate.RegisterClaim(ToolPlayerAction.Remove, "scatter-sample", ScatterPropsCommand.ShouldSuppressMiddleClick);



			HookConfigHotReload();

			// Install the owned YAML before Infinity Hammer initializes its file watcher.
			// This avoids a runtime watcher reload that can duplicate tool entries.
			if (PluginConfig.Enabled != null && PluginConfig.Enabled.Value
			    && PluginConfig.InstallInfinityHammerTools != null
			    && PluginConfig.InstallInfinityHammerTools.Value)
			{
				ToolInstallerBridge.EnsureInstalled();
			}

			TryInitializeInfinityHammerIntegration(logIfMissing: false);



			ModLogger.LogInfo(PluginName + " " + PluginVersion + " loaded.");

		}



		private void Update()

		{
			ToolRuntime.BeginFrame();
			// If this MonoBehaviour updates before Player.Update, snapshot input now,
			// before vanilla's Player.Update can consume the same mouse/key edge.
			// The Player.Update prefix is the other early entry point; the per-frame
			// cache makes the first capture authoritative for every tool this frame.
			ToolInput.CaptureFrame();
			// The chainloader honours [BepInDependency], but ScriptEngine's LoadOnStart can
			// awaken this plugin mid-chainload before IH. The first Update runs after the
			// chainloader finishes, so one retry here is enough.
			if (!_infinityHammerInitialized && !_infinityHammerRetried)
			{
				_infinityHammerRetried = true;
				TryInitializeInfinityHammerIntegration(logIfMissing: true);
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



		private void TryInitializeInfinityHammerIntegration(bool logIfMissing)
		{
			if (_infinityHammerInitialized)
			{
				return;
			}

			if (!InfinityHammerBridge.TryDetect(out _, out _))
			{
				if (logIfMissing)
					ModLogger.LogError("Infinity Hammer (" + InfinityHammerGuid + ") not found after plugin loading; SonicWorldTools tools will not be available.");
				return;
			}

			_infinityHammerInitialized = true;
			DependencyDetector.Run();
			TerrainToolIconOverrides.TryInstall(_harmony);
			ScatterPropsCommand.Register();
			PrefabGeometryDumpCommand.Register();
			PrefabPlacementMetadata.RegisterRefreshCommand();
			_pieceRepeaterController = _pieceRepeaterController ?? new PieceRepeaterController();
			ToolSessionCoordinator.Register(_pieceRepeaterController);
			ToolInputRouter.RegisterEarlyUpdateHandler(
				"line-tool-settings",
				_ => PieceRepeaterController.TryConsumeSettingsHotkey());
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
				// IH can temporarily disappear during plugin reloads or its own tool reset.
				// Keep the feature controller alive; only tool selection is unavailable
				// during that interval. Unregistering here left it permanently inert
				// because integration initialization is one-shot.
				ModLogger.LogDebug("Infinity Hammer temporarily unavailable; preserving Precision Select controller.");
				return;

			}



			if (_pipetteController == null)

			{

				_pipetteController = new PrecisionAreaPipetteController();

			}

			ToolSessionCoordinator.Register(_pipetteController);



			PrecisionAreaPipetteCommand.Register();

		}



		private void UnregisterPrecisionAreaPipette()

		{

			if (_pipetteController != null)

			{

				_pipetteController.Deactivate();

			}



			ToolSessionCoordinator.Unregister(_pipetteController);
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
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterTraceKey);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterTraceModifier);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterSettingsHotkey);
			HookGeneratedToolSetting(PluginConfig.PieceRepeaterOffsetEditorModifier);
			HookGeneratedToolSetting(PluginConfig.ScatterSampleKey);
			HookGeneratedToolSetting(PluginConfig.ScatterClearModifier);
			HookGeneratedToolSetting(PluginConfig.TerrainPathInitialHalfWidth);
			HookGeneratedToolSetting(PluginConfig.TerrainPathEdgeSmoothness);
			HookGeneratedToolSetting(PluginConfig.TerrainPathFinishKey);
			HookGeneratedToolSetting(PluginConfig.TerrainPathSmoothKey);
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
			ToolSessionCoordinator.Reset();
			ToolActionGate.Reset();
			ToolInputRouter.Reset();
			ToolRuntime.Reset();
			PrefabPlacementMetadata.Shutdown();
			ScatterPreview.Shutdown();
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
