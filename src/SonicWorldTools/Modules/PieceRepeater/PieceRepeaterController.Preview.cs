using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SonicWorldTools.Config;
using SonicWorldTools.Core;
using SonicWorldTools.Integration;
using SonicWorldTools.Modules.PrecisionAreaPipette;
using UnityEngine;
using Jotunn.Configs;
using Jotunn.Managers;
using BepInEx.Configuration;

namespace SonicWorldTools.Modules.PieceRepeater
{
	/// <summary>Preview line renderers and translucent piece ghosts.</summary>
	internal sealed partial class PieceRepeaterController
	{
		private void EnsurePreview()
		{
			if (_previewRoot) return;
			_previewRoot = new GameObject("SonicWorldTools_PieceRepeaterPreview") { hideFlags = HideFlags.HideAndDontSave };
			UnityEngine.Object.DontDestroyOnLoad(_previewRoot);
			Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
			_previewMaterial = new Material(shader) { color = new Color(0.25f, 0.9f, 1f, 0.85f), hideFlags = HideFlags.HideAndDontSave };
			_pieceGhostMaterial = new Material(shader) { color = new Color(0.35f, 0.9f, 1f, 0.42f), hideFlags = HideFlags.HideAndDontSave };
			_outlineLine = CreateLine("Outline", 0.035f);
		}

		private LineRenderer CreateLine(string name, float width)
		{
			var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
			go.transform.SetParent(_previewRoot.transform, false);
			var line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.material = _previewMaterial;
			line.startColor = line.endColor = new Color(0.25f, 0.9f, 1f, 0.85f);
			line.startWidth = line.endWidth = width;
			line.enabled = false;
			return line;
		}

		private void UpdatePreview()
		{
			if (!_active) return;
			EnsurePreview();
			_placements.Clear();
			_outline.Clear();
			if (_points.Count > 0 && _hasAim && ZNetScene.instance && !string.IsNullOrEmpty(_prefabId))
			{
				GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
				if (prefab)
				{
					if (_mode == PieceRepeaterMode.Path)
					{
						var pending = new List<Vector3>(_points);
						// A traced row is a complete route; the cursor does not extend it.
						if (!IsTracing)
						{
							Vector3 previewWaypoint = GetPathWaypoint(_aim, pending);
							Vector3 previewLeg = previewWaypoint - pending[pending.Count - 1];
							previewLeg.y = 0f;
							// A repeated endpoint creates a degenerate Catmull-Rom segment
							// that can loop beyond the last committed point. Keep the preview
							// identical to the committed route until the cursor advances.
							if (previewLeg.sqrMagnitude >= 0.0001f) pending.Add(previewWaypoint);
						}
						if (pending.Count >= 2) GeneratePathPreview(pending, GetSpacing(prefab), prefab);
					}
					else if (_mode == PieceRepeaterMode.Circle || _points.Count >= 1)
					{
						var original = _points;
						GenerateLayoutWithAim(prefab, original[0], _aim);
					}
				}
			}
			if (_points.Count == 0 && _hasAim)
			{
				float radius = Mathf.Max(0.25f, GetSpacing() * 0.25f);
				for (int i = 0; i < 20; i++)
				{
					float angle = 2f * Mathf.PI * i / 20f;
					_outline.Add(_aim + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
				}
			}
			SetLine(_outlineLine, _outline, _mode != PieceRepeaterMode.Path || _points.Count == 0);
			UpdatePieceGhosts();
			RefreshShapeToolCard();
		}

		private void SetLine(LineRenderer line, IList<Vector3> points, bool loop)
		{
			if (!line) return;
			if (points == null || points.Count < 2)
			{
				line.enabled = false;
				return;
			}
			line.positionCount = points.Count;
			for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i] + Vector3.up * 0.06f);
			line.loop = loop;
			line.enabled = true;
		}

		private void UpdatePieceGhosts()
		{
			GameObject prefab = ZNetScene.instance && !string.IsNullOrEmpty(_prefabId)
				? ZNetScene.instance.GetPrefab(_prefabId)
				: null;
			if (!prefab || !_hasAim)
			{
				SetPieceGhostCount(0);
				return;
			}

			// In the offset editor, let the real piece behind the preview remain
			// readable so small join and root corrections can be judged against it.
			if (_pieceGhostMaterial)
				_pieceGhostMaterial.color = _offsetEditor != null && _offsetEditor.IsOpen
					? new Color(0.42f, 0.62f, 0.68f, 0.14f)
					: new Color(0.35f, 0.82f, 0.9f, 0.28f);

			if (_pieceGhostPrefabId != _prefabId)
			{
				DestroyPieceGhosts();
				_pieceGhostPrefabId = _prefabId;
			}

			var visiblePlacements = new List<Placement>();
			if (_placements.Count > 0)
			{
				int count = Mathf.Min(_placements.Count, MaximumPreviewDots);
				for (int i = 0; i < count; i++) visiblePlacements.Add(_placements[i]);
			}
			else if (_active)
			{
				if (_points.Count > 0 && _hasLockedStartPlacement)
				{
					// With only the first waypoint set, keep showing its committed pose.
					// Never fall back to a cursor-following ghost after the click.
					visiblePlacements.Add(_lockedStartPlacement);
				}
				else
				{
					ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
					Placement initial = AnarchyModeActive
						? GetAnarchyInitialPlacement(_aim, metadata)
						: GetInitialPlacement(prefab, _aim, metadata);
					if (!AnarchyModeActive
					    && TryGetInitialSnapPlacement(Player.m_localPlayer, _aim, out Placement snappedPose))
						initial = snappedPose;
					visiblePlacements.Add(initial);
				}
			}

			while (_pieceGhosts.Count < visiblePlacements.Count)
				_pieceGhosts.Add(CreatePieceGhost(prefab));
			SetPieceGhostCount(visiblePlacements.Count);
			for (int i = 0; i < visiblePlacements.Count; i++)
			{
				Transform ghost = _pieceGhosts[i].transform;
				ghost.SetPositionAndRotation(visiblePlacements[i].Position, visiblePlacements[i].Rotation);
			}
		}

		private GameObject CreatePieceGhost(GameObject prefab)
		{
			return SonicWorldTools.Rendering.PrefabGhostFactory.Create(prefab, _previewRoot.transform, _pieceGhostMaterial, "PieceRepeaterPieceGhost");
		}

		private void SetPieceGhostCount(int count)
		{
			for (int i = 0; i < _pieceGhosts.Count; i++)
				if (_pieceGhosts[i]) _pieceGhosts[i].SetActive(i < count);
		}

		private void DestroyPieceGhosts()
		{
			for (int i = 0; i < _pieceGhosts.Count; i++)
				if (_pieceGhosts[i]) UnityEngine.Object.Destroy(_pieceGhosts[i]);
			_pieceGhosts.Clear();
		}

		private void ClearPreview()
		{
			ClearSnapProbe();
			if (_previewRoot) UnityEngine.Object.Destroy(_previewRoot);
			if (_previewMaterial) UnityEngine.Object.Destroy(_previewMaterial);
			if (_pieceGhostMaterial) UnityEngine.Object.Destroy(_pieceGhostMaterial);
			_previewRoot = null;
			_outlineLine = null;
			_previewMaterial = null;
			_pieceGhostMaterial = null;
			_pieceGhostPrefabId = null;
			_pieceGhosts.Clear();
		}
	}
}
