using System;
using Jotunn.Managers;
using SonicWorldTools.Config;
using UnityEngine;
using UnityEngine.UI;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>On-demand Valheim-styled options; never accepts input during placement.</summary>
	internal sealed class PieceRepeaterSettingsPanel
	{
		private readonly PieceRepeaterController _controller;
		private GameObject _panel;
		private bool _open;
		private Text _curveValue;
		private Text _radiusValue;
		private Text _gapValue;
		private Text _heightValue;
		private Text _facingValue;

		internal bool IsOpen => _open;

		internal PieceRepeaterSettingsPanel(PieceRepeaterController controller)
		{
			_controller = controller;
			if (!GUIManager.IsHeadless()) GUIManager.OnCustomGUIAvailable += OnGuiReady;
		}

		internal void Open()
		{
			if (GUIManager.IsHeadless()) return;
			if (!_panel) Build();
			if (!_panel) return;
			RefreshLabels();
			_open = true;
			_panel.SetActive(true);
			GUIManager.BlockInput(true);
			Input.ResetInputAxes();
		}

		internal void Close(bool consumeInput)
		{
			if (!_open) return;
			_open = false;
			if (_panel) _panel.SetActive(false);
			if (consumeInput) Input.ResetInputAxes();
			GUIManager.BlockInput(false);
			_controller.OnSettingsClosed();
		}

		internal void Shutdown()
		{
			GUIManager.OnCustomGUIAvailable -= OnGuiReady;
			Close(false);
			if (_panel) UnityEngine.Object.Destroy(_panel);
			_panel = null;
		}

		private void OnGuiReady()
		{
			Close(false);
			_panel = null; // Valheim recreated CustomGUIFront for this scene.
			Build();
		}

		private void Build()
		{
			if (GUIManager.Instance == null || !GUIManager.CustomGUIFront) return;
			GUIManager gui = GUIManager.Instance;
			_panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
				new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
				530f, 370f, true);
			_panel.SetActive(false);
			Label("Line Tool settings", 0f, -34f, 28, 490f, gui.ValheimOrange);
			Label("Changes update the preview. Placement pauses while this is open.", 0f, -66f, 14, 490f, Color.white);

			Label("Path interpolation", -145f, -112f, 18, 190f, Color.white);
			_curveValue = Label("", 60f, -112f, 18, 150f, Color.white);
			Button("Change", 190f, -112f, 88f, () =>
			{
				PluginConfig.PieceRepeaterRoundedCorners.Value = !PluginConfig.PieceRepeaterRoundedCorners.Value;
				Changed();
			});

			Label("Smoothing distance", -145f, -158f, 18, 190f, Color.white);
			Button("-", 5f, -158f, 34f, () => ChangeRadius(-0.25f));
			_radiusValue = Label("", 85f, -158f, 18, 90f, Color.white);
			Button("+", 162f, -158f, 34f, () => ChangeRadius(0.25f));

			Label("Spacing offset", -145f, -204f, 18, 150f, Color.white);
			Button("-", 5f, -204f, 34f, () => { _controller.SetGap(_controller.GapMeters - 0.1f); RefreshLabels(); });
			_gapValue = Label("", 85f, -204f, 18, 90f, Color.white);
			Button("+", 162f, -204f, 34f, () => { _controller.SetGap(_controller.GapMeters + 0.1f); RefreshLabels(); });

			Label("Height", -145f, -250f, 18, 150f, Color.white);
			_heightValue = Label("", 60f, -250f, 18, 150f, Color.white);
			Button("Change", 190f, -250f, 88f, () =>
			{
				PluginConfig.PieceRepeaterLevelByDefault.Value = !PluginConfig.PieceRepeaterLevelByDefault.Value;
				Changed();
			});

			Label("Prop facing", -145f, -296f, 18, 150f, Color.white);
			_facingValue = Label("", 60f, -296f, 18, 150f, Color.white);
			Button("Change", 190f, -296f, 88f, () =>
			{
				PluginConfig.PieceRepeaterPropFollowCurve.Value = !PluginConfig.PieceRepeaterPropFollowCurve.Value;
				Changed();
			});

			Button("Close", 0f, -340f, 110f, () => Close(true));
			RefreshLabels();
		}

		private Text Label(string text, float x, float y, int size, float width, Color color)
		{
			GameObject go = GUIManager.Instance.CreateText(text, _panel.transform,
				new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y),
				GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, width, 36f, false);
			Text label = go.GetComponent<Text>();
			if (label) label.alignment = TextAnchor.MiddleCenter;
			return label;
		}

		private void Button(string text, float x, float y, float width, Action action)
		{
			GameObject go = GUIManager.Instance.CreateButton(text, _panel.transform,
				new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), width, 36f);
			UnityEngine.UI.Button button = go.GetComponent<UnityEngine.UI.Button>();
			if (button) button.onClick.AddListener(() => action());
		}

		private void ChangeRadius(float delta)
		{
			float value = Mathf.Clamp(PluginConfig.PieceRepeaterCornerRadius.Value + delta, 0.25f, 6f);
			PluginConfig.PieceRepeaterCornerRadius.Value = Mathf.Round(value * 4f) / 4f;
			Changed();
		}

		private void Changed()
		{
			RefreshLabels();
			_controller.RefreshSettingsPreview();
		}

		private void RefreshLabels()
		{
			if (_curveValue) _curveValue.text = PluginConfig.PieceRepeaterRoundedCorners.Value ? "Catmull-Rom" : "Linear";
			if (_radiusValue) _radiusValue.text = PluginConfig.PieceRepeaterCornerRadius.Value.ToString("0.##") + " m";
			if (_gapValue) _gapValue.text = _controller.GapMeters.ToString("0.##") + " m";
			if (_heightValue) _heightValue.text = PluginConfig.PieceRepeaterLevelByDefault.Value ? "Level" : "Follow surface";
			if (_facingValue) _facingValue.text = PluginConfig.PieceRepeaterPropFollowCurve.Value ? "Tangent" : "Fixed";
		}
	}
}
