using System;
using System.Globalization;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Per-prefab placement corrections persisted into the profile's editable catalog.</summary>
	internal sealed class PieceRepeaterOffsetEditor
	{
		private readonly PieceRepeaterController _controller;
		private GameObject _panel;
		private bool _open;
		private string _prefabId;
		private Vector3 _positionOffset;
		private Vector3 _joinStartOffset;
		private Vector3 _joinEndOffset;
		private float _yawOffset;
		private float _tangentYawOffset;
		private float _bottomOffset;
		private int _stepIndex;
		private Text _pieceLabel;
		private Text _statusLabel;
		private Text _stepLabel;
		private Text _positionXValue;
		private Text _positionYValue;
		private Text _positionZValue;
		private readonly Text[] _joinStartValues = new Text[3];
		private readonly Text[] _joinEndValues = new Text[3];
		private Text _yawValue;
		private Text _tangentYawValue;
		private Text _bottomValue;

		private static readonly float[] PositionSteps = { 0.001f, 0.01f, 0.1f };
		private static readonly float[] AngleSteps = { 0.1f, 1f, 5f };
		private static readonly string[] StepNames = { "Fine", "Medium", "Coarse" };
		internal bool IsOpen => _open;

		internal PieceRepeaterOffsetEditor(PieceRepeaterController controller)
		{
			_controller = controller;
			if (!GUIManager.IsHeadless()) GUIManager.OnCustomGUIAvailable += OnGuiReady;
		}

		internal void Open(string prefabId)
		{
			if (GUIManager.IsHeadless()) return;
			if (!_panel) Build();
			if (!_panel) return;
			_prefabId = prefabId;
			_pieceLabel.text = string.IsNullOrWhiteSpace(prefabId) ? "No sampled piece — MMB a build piece first" : "Prefab: " + prefabId;
			_statusLabel.text = string.Empty;
			bool hasOffsets = PrefabPlacementMetadata.TryGetEditableOffsets(prefabId,
				out _positionOffset, out _joinStartOffset, out _joinEndOffset,
				out _yawOffset, out _tangentYawOffset, out _bottomOffset);
			if (!hasOffsets)
			{
				_positionOffset = Vector3.zero;
				_joinStartOffset = _joinEndOffset = Vector3.zero;
				_yawOffset = _tangentYawOffset = _bottomOffset = 0f;
			}
			RefreshValues();
			_open = true;
			_panel.SetActive(true);
			GUIManager.BlockInput(true);
			Input.ResetInputAxes();
		}

		internal void Close(bool consumeInput)
		{
			if (!_open) return;
			PrefabPlacementMetadata.ClearPreviewOffsets(_prefabId);
			_open = false;
			if (_panel) _panel.SetActive(false);
			if (consumeInput) Input.ResetInputAxes();
			GUIManager.BlockInput(false);
			_controller.OffsetEditorClosed();
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
			_panel = null;
			Build();
		}

		private void Build()
		{
			if (GUIManager.Instance == null || !GUIManager.CustomGUIFront) return;
			GUIManager gui = GUIManager.Instance;
			_panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
				new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
				560f, 690f, true);
			_panel.SetActive(false);
			Label("Piece offsets", 0f, -27f, 26, 490f, gui.ValheimOrange);
			Label("Local X/Y/Z join edits re-solve later pieces; root offset repeats on each piece.", 0f, -58f, 14, 520f, Color.white);
			_pieceLabel = Label("", 0f, -87f, 17, 490f, Color.white);

			_stepLabel = Label("", -45f, -119f, 16, 220f, Color.white);
			Button("Step", 125f, -117f, 88f, CycleStep);

			Label("Join start correction — local metres", 0f, -158f, 18, 480f, Color.white);
			VectorControlRow(_joinStartValues, -194f, (axis, delta) => ChangeJoinOffset(true, axis, delta));
			Label("Join end correction — local metres", 0f, -246f, 18, 480f, Color.white);
			VectorControlRow(_joinEndValues, -282f, (axis, delta) => ChangeJoinOffset(false, axis, delta));
			Label("Whole-piece root correction (moves every repeat)", 0f, -333f, 16, 490f, Color.white);
			AxisRow("X", -117f, -367f, out _positionXValue,
				() => ChangePosition(0, -PositionSteps[_stepIndex]), () => ChangePosition(0, PositionSteps[_stepIndex]));
			AxisRow("Y", 0f, -400f, out _positionYValue,
				() => ChangePosition(1, -PositionSteps[_stepIndex]), () => ChangePosition(1, PositionSteps[_stepIndex]));
			AxisRow("Z", 117f, -433f, out _positionZValue,
				() => ChangePosition(2, -PositionSteps[_stepIndex]), () => ChangePosition(2, PositionSteps[_stepIndex]));

			ValueRow("Piece yaw correction", "°", -471f, out _yawValue,
				() => ChangeAngle(ref _yawOffset, -AngleSteps[_stepIndex]), () => ChangeAngle(ref _yawOffset, AngleSteps[_stepIndex]));
			ValueRow("Tangent-facing correction", "°", -510f, out _tangentYawValue,
				() => ChangeAngle(ref _tangentYawOffset, -AngleSteps[_stepIndex]), () => ChangeAngle(ref _tangentYawOffset, AngleSteps[_stepIndex]));
			ValueRow("Ground-contact offset", "m", -549f, out _bottomValue,
				() => ChangeBottom(-PositionSteps[_stepIndex]), () => ChangeBottom(PositionSteps[_stepIndex]));

			_statusLabel = Label("", 0f, -592f, 13, 500f, Color.white);
			Button("Save", -66f, -632f, 112f, Save);
			Button("Cancel", 66f, -632f, 112f, Cancel);
			RefreshValues();
		}

		private void AxisRow(string axis, float centerX, float y, out Text value,
			Action decrement, Action increment)
		{
			Label(axis, centerX - 73f, y - 2f, 17, 28f, Color.white);
			Button("−", centerX - 34f, y, 34f, decrement);
			value = Label("0.000", centerX + 4f, y - 1f, 17, 68f, Color.white);
			Button("+", centerX + 49f, y, 34f, increment);
		}

		private void VectorControlRow(Text[] values, float y, Action<int, float> change)
		{
			string[] axes = { "X", "Y", "Z" };
			for (int axis = 0; axis < 3; axis++)
			{
				float centerX = (axis - 1) * 155f;
				Label(axes[axis], centerX, y + 11f, 14, 44f, Color.white);
				int capturedAxis = axis;
				Button("−", centerX - 47f, y - 13f, 34f,
					() => change(capturedAxis, -PositionSteps[_stepIndex]));
				values[axis] = Label("0.000", centerX, y - 13f, 14, 54f, Color.white);
				Button("+", centerX + 47f, y - 13f, 34f,
					() => change(capturedAxis, PositionSteps[_stepIndex]));
			}
		}

		private void ValueRow(string label, string unit, float y, out Text value,
			Action decrement, Action increment)
		{
			Label(label, -115f, y - 2f, 16, 245f, Color.white);
			Button("−", 50f, y, 36f, decrement);
			value = Label("0.000 " + unit, 128f, y - 1f, 17, 108f, Color.white);
			Button("+", 206f, y, 36f, increment);
		}

		private Text Label(string text, float x, float y, int size, float width, Color color)
		{
			GameObject go = GUIManager.Instance.CreateText(text, _panel.transform,
				new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y),
				GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, width, 30f, false);
			Text label = go.GetComponent<Text>();
			if (label) label.alignment = TextAnchor.MiddleCenter;
			return label;
		}

		private void Button(string text, float x, float y, float width, Action action)
		{
			GameObject go = GUIManager.Instance.CreateButton(text, _panel.transform,
				new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), width, 32f);
			Button button = go.GetComponent<Button>();
			if (button) button.onClick.AddListener(() => action());
		}

		private void CycleStep()
		{
			_stepIndex = (_stepIndex + 1) % StepNames.Length;
			RefreshValues();
		}

		private void ChangePosition(int axis, float delta)
		{
			if (axis == 0) _positionOffset.x = Mathf.Round((_positionOffset.x + delta) * 1000f) / 1000f;
			else if (axis == 1) _positionOffset.y = Mathf.Round((_positionOffset.y + delta) * 1000f) / 1000f;
			else _positionOffset.z = Mathf.Round((_positionOffset.z + delta) * 1000f) / 1000f;
			RefreshValues();
			UpdateLivePreview();
		}

		private void ChangeJoinOffset(bool start, int axis, float delta)
		{
			Vector3 value = start ? _joinStartOffset : _joinEndOffset;
			if (axis == 0) value.x = Mathf.Round((value.x + delta) * 1000f) / 1000f;
			else if (axis == 1) value.y = Mathf.Round((value.y + delta) * 1000f) / 1000f;
			else value.z = Mathf.Round((value.z + delta) * 1000f) / 1000f;
			if (start) _joinStartOffset = value;
			else _joinEndOffset = value;
			RefreshValues();
			UpdateLivePreview();
		}

		private void ChangeAngle(ref float value, float delta)
		{
			value = Mathf.Round((value + delta) * 10f) / 10f;
			RefreshValues();
			UpdateLivePreview();
		}

		private void ChangeBottom(float delta)
		{
			_bottomOffset = Mathf.Round((_bottomOffset + delta) * 1000f) / 1000f;
			RefreshValues();
			UpdateLivePreview();
		}

		private void UpdateLivePreview()
		{
			PrefabPlacementMetadata.SetPreviewOffsets(_prefabId, _positionOffset,
				_joinStartOffset, _joinEndOffset, _yawOffset, _tangentYawOffset, _bottomOffset);
			_controller.RefreshOffsetPreview();
			if (_statusLabel) _statusLabel.text = "Preview updated. Save to keep these offsets.";
		}

		private void RefreshValues()
		{
			if (_stepLabel) _stepLabel.text = "Step: " + StepNames[_stepIndex] + "  "
				+ PositionSteps[_stepIndex].ToString("0.###", CultureInfo.InvariantCulture) + " m / "
				+ AngleSteps[_stepIndex].ToString("0.#", CultureInfo.InvariantCulture) + "°";
			if (_positionXValue) _positionXValue.text = _positionOffset.x.ToString("0.000", CultureInfo.InvariantCulture);
			if (_positionYValue) _positionYValue.text = _positionOffset.y.ToString("0.000", CultureInfo.InvariantCulture);
			if (_positionZValue) _positionZValue.text = _positionOffset.z.ToString("0.000", CultureInfo.InvariantCulture);
			for (int axis = 0; axis < 3; axis++)
			{
				float start = axis == 0 ? _joinStartOffset.x : axis == 1 ? _joinStartOffset.y : _joinStartOffset.z;
				float end = axis == 0 ? _joinEndOffset.x : axis == 1 ? _joinEndOffset.y : _joinEndOffset.z;
				if (_joinStartValues[axis]) _joinStartValues[axis].text = start.ToString("0.000", CultureInfo.InvariantCulture);
				if (_joinEndValues[axis]) _joinEndValues[axis].text = end.ToString("0.000", CultureInfo.InvariantCulture);
			}
			if (_yawValue) _yawValue.text = _yawOffset.ToString("0.0", CultureInfo.InvariantCulture) + "°";
			if (_tangentYawValue) _tangentYawValue.text = _tangentYawOffset.ToString("0.0", CultureInfo.InvariantCulture) + "°";
			if (_bottomValue) _bottomValue.text = _bottomOffset.ToString("0.000", CultureInfo.InvariantCulture) + " m";
		}

		private void Save()
		{
			if (!PrefabPlacementMetadata.SaveEditableOffsets(_prefabId, _positionOffset,
				_joinStartOffset, _joinEndOffset, _yawOffset,
				_tangentYawOffset, _bottomOffset, out string error))
			{
				_statusLabel.text = error;
				return;
			}
			_statusLabel.text = "Saved for " + _prefabId + ". Preview updated.";
			_controller.RefreshOffsetPreview();
		}

		private void Cancel()
		{
			Close(true);
		}
	}
}
