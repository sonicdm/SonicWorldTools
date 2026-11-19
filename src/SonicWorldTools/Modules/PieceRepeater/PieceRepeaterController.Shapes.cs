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
	/// <summary>Shape Tool layouts (circle, square, rectangle; outline/grid/concentric fill) and the shape card text.</summary>
	internal sealed partial class PieceRepeaterController
	{
		private void GenerateCircleOutline(Vector3 center, float radius, float spacing)
		{
			int count = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / spacing), 3, MaximumPlacements);
			for (int i = 0; i < count; i++)
			{
				float angle = 2f * Mathf.PI * i / count;
				Vector3 pos = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
				float tangent = angle * Mathf.Rad2Deg + 90f;
				_placements.Add(new Placement(pos, Quaternion.Euler(0f, tangent, 0f)));
				_outline.Add(pos);
			}
		}

		private void GenerateCircleFill(Vector3 center, float radius, float spacing)
		{
			// Build radial rows instead of a staggered Cartesian/hex grid so the
			// infill follows the same circular form as its boundary.
			_placements.Add(new Placement(center, Quaternion.identity));
			float ringStep = spacing * 0.8660254f;
			int ringCount = Mathf.CeilToInt(radius / ringStep);
			for (int ring = 1; ring <= ringCount; ring++)
			{
				float ringRadius = Mathf.Min(radius, ring * ringStep);
				int count = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * ringRadius / spacing));
				float angularOffset = (ring & 1) == 0 ? Mathf.PI / count : 0f;
				for (int i = 0; i < count; i++)
				{
					float angle = angularOffset + 2f * Mathf.PI * i / count;
					Vector3 position = center + new Vector3(Mathf.Sin(angle) * ringRadius, 0f, Mathf.Cos(angle) * ringRadius);
					Quaternion tangent = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + 90f, 0f);
					_placements.Add(new Placement(position, tangent));
					if (_placements.Count > MaximumPlacements) return;
				}
			}
			int segments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / spacing), 12, 96);
			for (int i = 0; i < segments; i++)
			{
				float angle = 2f * Mathf.PI * i / segments;
				_outline.Add(center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
			}
		}

		private void GenerateRectangle(Vector3 a, Vector3 b, float spacing, GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			// Rectangles start at the first-click center, just like circles and squares.
			float halfX = Mathf.Max(spacing * 0.5f, Mathf.Round(Mathf.Abs(b.x - a.x) * 2f / spacing) * spacing * 0.5f);
			float halfZ = Mathf.Max(spacing * 0.5f, Mathf.Round(Mathf.Abs(b.z - a.z) * 2f / spacing) * spacing * 0.5f);
			float minX = a.x - halfX;
			float maxX = a.x + halfX;
			float minZ = a.z - halfZ;
			float maxZ = a.z + halfZ;

			Vector3[] corners =
			{
				new Vector3(minX, a.y, minZ), new Vector3(maxX, a.y, minZ),
				new Vector3(maxX, a.y, maxZ), new Vector3(minX, a.y, maxZ)
			};
			if (_shapeFillMode == ShapeFillMode.Grid)
			{
				GenerateFilledRectangle(a, Vector3.right, Vector3.forward, halfX, halfZ, spacing, metadata);
			}
			else if (_shapeFillMode == ShapeFillMode.Concentric)
			{
				GenerateConcentricRectangle(a, Vector3.right, Vector3.forward, halfX, halfZ, spacing,
					GetShapeRingSpacing(prefab, metadata), prefab, metadata);
			}
			else
			{
				GenerateOutlineEdges(corners, spacing, prefab, metadata);
			}
			_outline.AddRange(corners);
		}

		private void GenerateFilledRectangle(Vector3 center, Vector3 axisX, Vector3 axisZ,
			float halfX, float halfZ, float spacing, ResolvedPrefabPlacement metadata)
		{
			int countX = Mathf.Max(1, Mathf.RoundToInt(halfX * 2f / spacing));
			int countZ = Mathf.Max(1, Mathf.RoundToInt(halfZ * 2f / spacing));
			float stepX = halfX * 2f / countX;
			float stepZ = halfZ * 2f / countZ;
			Vector3 forward = metadata.Axis == PrefabRepeatAxis.X ? axisX : axisZ;
			Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
			for (int z = 0; z < countZ; z++)
			{
				float localZ = -halfZ + (z + 0.5f) * stepZ;
				for (int x = 0; x < countX; x++)
				{
					float localX = -halfX + (x + 0.5f) * stepX;
					Vector3 position = center + axisX * localX + axisZ * localZ;
					_placements.Add(new Placement(position, rotation));
					if (_placements.Count > MaximumPlacements) return;
				}
			}
		}

		private void GenerateConcentricRectangle(Vector3 center, Vector3 axisX, Vector3 axisZ,
			float halfX, float halfZ, float spacing, float ringStep,
			GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			var rings = new List<Vector3[]>();
			int requiredPlacements = 0;
			bool addCenterPiece = false;
			for (float inset = 0f; halfX - inset > 0f && halfZ - inset > 0f; inset += ringStep)
			{
				float ringHalfX = halfX - inset;
				float ringHalfZ = halfZ - inset;
				if (ringHalfX * 2f < spacing * 1.5f && ringHalfZ * 2f < spacing * 1.5f)
				{
					addCenterPiece = true;
					requiredPlacements++;
					if (requiredPlacements > MaximumPlacements)
					{
						_shapeLayoutOverLimit = true;
						_placements.Clear();
						return;
					}
					break;
				}
				if (ringHalfX * 2f < spacing * 1.5f || ringHalfZ * 2f < spacing * 1.5f)
					break;
				Vector3[] corners =
				{
					center - axisX * ringHalfX - axisZ * ringHalfZ,
					center + axisX * ringHalfX - axisZ * ringHalfZ,
					center + axisX * ringHalfX + axisZ * ringHalfZ,
					center - axisX * ringHalfX + axisZ * ringHalfZ
				};
				int edgeX = Mathf.Max(1, Mathf.RoundToInt(ringHalfX * 2f / spacing));
				int edgeZ = Mathf.Max(1, Mathf.RoundToInt(ringHalfZ * 2f / spacing));
				requiredPlacements += 2 * edgeX + 2 * edgeZ;
				if (requiredPlacements > MaximumPlacements)
				{
					_shapeLayoutOverLimit = true;
					_placements.Clear();
					return;
				}
				rings.Add(corners);
			}

			foreach (Vector3[] ring in rings)
				GenerateOutlineEdges(ring, spacing, prefab, metadata);
			if (addCenterPiece)
			{
				Vector3 forward = metadata.Axis == PrefabRepeatAxis.X ? axisX : axisZ;
				_placements.Add(new Placement(center, Quaternion.LookRotation(forward, Vector3.up)));
			}
		}

		private float GetShapeRingSpacing(GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			return Mathf.Max(MinimumSpacing, GetShapeRingSpacingBase(prefab, metadata) + _shapeRingSpacingAdjustment);
		}

		private static float GetShapeRingSpacingBase(GameObject prefab, ResolvedPrefabPlacement metadata)
		{
			if (!prefab) return MinimumSpacing;
			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float across = metadata.Axis == PrefabRepeatAxis.X ? bounds.size.z
				: metadata.Axis == PrefabRepeatAxis.Z ? bounds.size.x
				: Mathf.Min(bounds.size.x, bounds.size.z);
			if (float.IsNaN(across) || float.IsInfinity(across) || across < 0.001f)
				return Mathf.Max(MinimumSpacing, metadata.Spacing * 0.5f);
			return Mathf.Max(MinimumSpacing, Mathf.Min(metadata.Spacing, across));
		}

		private string GetShapeFillLabel()
		{
			if (_shapeFillMode == ShapeFillMode.Outline) return "outline only";
			if (_mode == PieceRepeaterMode.Circle) return "concentric infill";
			return _shapeFillMode == ShapeFillMode.Grid ? "grid infill" : "concentric infill";
		}

		private string GetShapeSizeLabel(float pieceSpacing)
		{
			if (_points.Count == 0 || !_hasAim) return "unset";
			Vector3 center = _points[0];
			float dx = Mathf.Abs(_aim.x - center.x);
			float dz = Mathf.Abs(_aim.z - center.z);
			if (_mode == PieceRepeaterMode.Circle)
			{
				float diameter = Vector2.Distance(new Vector2(center.x, center.z), new Vector2(_aim.x, _aim.z)) * 2f;
				return "diameter " + FormatMeters(diameter);
			}

			if (_mode == PieceRepeaterMode.Square)
			{
				float requestedSide = Mathf.Sqrt(dx * dx + dz * dz) * Mathf.Sqrt(2f);
				float side = Mathf.Max(pieceSpacing, Mathf.Round(requestedSide / pieceSpacing) * pieceSpacing);
				return "side " + FormatMeters(side);
			}

			float width = Mathf.Max(pieceSpacing, Mathf.Round(dx * 2f / pieceSpacing) * pieceSpacing);
			float depth = Mathf.Max(pieceSpacing, Mathf.Round(dz * 2f / pieceSpacing) * pieceSpacing);
			return FormatMeters(width) + " × " + FormatMeters(depth);
		}

		private static string FormatMeters(float value)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture) + " m";
		}

		private void RefreshShapeToolCard()
		{
			if (!_shapeToolMode) return;
			if (!InfinityHammerBridge.TryGetActiveToolSelectionPiece(ToolInstaller.ShapeToolName, out Piece piece) || !piece)
			{
				_shapeDescriptionPiece = null;
				_shapeDescriptionValue = null;
				return;
			}

			string description = piece.m_description ?? string.Empty;
			int markerIndex = description.IndexOf(ShapeDescriptionMarker, StringComparison.Ordinal);
			if (markerIndex >= 0) description = description.Substring(0, markerIndex);

			string liveValues;
			if (!string.IsNullOrEmpty(_prefabId) && ZNetScene.instance
			    && ZNetScene.instance.GetPrefab(_prefabId) is GameObject prefab)
			{
				ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
				float pieceSpacing = GetSpacing(prefab);
				liveValues = ShapeDescriptionMarker + _mode + " / " + GetShapeFillLabel()
					+ "\nSize: " + GetShapeSizeLabel(pieceSpacing)
					+ " · Preview: " + (_shapeLayoutOverLimit ? ">" + MaximumPlacements : _placements.Count.ToString(CultureInfo.InvariantCulture))
					+ "\nPiece spacing: " + FormatMeters(pieceSpacing) + " · Gap: " + FormatMeters(_gapMeters)
					+ "\nRotation: " + (_rotationSteps * RotationStepDegrees).ToString("0.#", CultureInfo.InvariantCulture) + "°";
				if (_shapeFillMode == ShapeFillMode.Concentric)
				{
					float ringSpacing = _mode == PieceRepeaterMode.Circle
						? pieceSpacing * 0.8660254f
						: GetShapeRingSpacing(prefab, metadata);
					liveValues += " · Ring spacing: " + FormatMeters(ringSpacing);
				}
			}
			else
			{
				liveValues = ShapeDescriptionMarker + _mode + " / " + GetShapeFillLabel()
					+ "\nSample a piece to show spacing and size.";
			}

			if (_shapeDescriptionPiece == piece && string.Equals(_shapeDescriptionValue, liveValues, StringComparison.Ordinal))
				return;
			if (_shapeDescriptionPiece == piece && Time.frameCount - _shapeDescriptionLastRefreshFrame < 6)
				return;

			piece.m_description = description + liveValues;
			try
			{
				if (Hud.instance && SetupPieceInfoMethod != null)
					SetupPieceInfoMethod.Invoke(Hud.instance, new object[] { piece });
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogDebug("Could not refresh Shape Tool card values: " + ex.Message);
			}
			_shapeDescriptionPiece = piece;
			_shapeDescriptionValue = liveValues;
			_shapeDescriptionLastRefreshFrame = Time.frameCount;
		}

		private void GenerateOutlineEdges(Vector3[] corners, float spacing, GameObject prefab,
			ResolvedPrefabPlacement metadata)
		{
			float inset = GetWidePanelInset(prefab, metadata, spacing, out bool squarePanel);
			float signedArea = 0f;
			for (int i = 0; i < corners.Length; i++)
			{
				Vector3 current = corners[i];
				Vector3 next = corners[(i + 1) % corners.Length];
				signedArea += current.x * next.z - next.x * current.z;
			}
			bool interiorOnLeft = signedArea >= 0f;
			for (int side = 0; side < corners.Length; side++)
			{
				Vector3 start = corners[side];
				Vector3 end = corners[(side + 1) % corners.Length];
				Vector3 edge = end - start;
				float length = edge.magnitude;
				if (length < MinimumSpacing * 0.5f) continue;
				int count = Mathf.Max(1, Mathf.RoundToInt(length / spacing));
				Vector3 dir = edge / length;
				Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
				Vector3 left = new Vector3(-dir.z, 0f, dir.x);
				Vector3 inward = interiorOnLeft ? left : -left;
				for (int i = 0; i < count; i++)
				{
					float t = (i + 0.5f) / count;
					Vector3 position = Vector3.Lerp(start, end, t) + inward * inset;
					if (squarePanel && i == 0 && side > 0 && _placements.Count > 0
					    && (_placements[_placements.Count - 1].Position - position).sqrMagnitude < 0.0025f)
						continue;
					_placements.Add(new Placement(position, rot));
					if (_placements.Count > MaximumPlacements) return;
				}
			}
		}

		private static float GetWidePanelInset(GameObject prefab, ResolvedPrefabPlacement metadata,
			float spacing, out bool squarePanel)
		{
			squarePanel = false;
			if (!prefab || metadata.ProfileType != RepeatProfileType.EdgeChain
			    || !metadata.SupportsX || !metadata.SupportsZ
			    || (metadata.Axis != PrefabRepeatAxis.X && metadata.Axis != PrefabRepeatAxis.Z)) return 0f;
			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float along = metadata.Axis == PrefabRepeatAxis.X ? bounds.size.x : bounds.size.z;
			float across = metadata.Axis == PrefabRepeatAxis.X ? bounds.size.z : bounds.size.x;
			if (along < spacing * 0.75f || across < spacing * 0.75f) return 0f;
			squarePanel = Mathf.Abs(along - across) < spacing * 0.1f;
			return across * 0.5f;
		}
	}
}
