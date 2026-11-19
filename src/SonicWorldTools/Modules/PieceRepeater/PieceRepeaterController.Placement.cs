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
	/// <summary>Start placement, snap points, height modes, terrain fill-down, committing placements and verifying them.</summary>
	internal sealed partial class PieceRepeaterController
	{
		private void BuildAndPlace(Player player)
		{
			if (!ZNetScene.instance)
			{
				Notify(ToolDisplayName + ": prefab scene is unavailable.");
				_points.Clear();
				return;
			}

			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			bool anarchy = AnarchyModeActive;
			if (!prefab || (!piece && !anarchy) || (anarchy && !prefab.GetComponent<ZNetView>()))
			{
				Notify(ToolDisplayName + ": selected prefab is unavailable or is not a networked spawnable object.");
				_prefabId = null;
				_points.Clear();
				return;
			}

			GenerateLayout(prefab);
			if (_shapeLayoutOverLimit)
			{
				Notify("Shape Tool: concentric infill exceeds the safe limit of "
					+ MaximumPlacements + " placements. Increase ring spacing or reduce the shape.");
				return;
			}
			if (_placements.Count == 0)
			{
				Notify(ToolDisplayName + ": add more points or increase the size.");
				_points.Clear();
				return;
			}
			if (_placements.Count > MaximumPlacements)
			{
				Notify(ToolDisplayName + ": " + _placements.Count + " placements exceeds the safe limit of " + MaximumPlacements + ". Increase spacing or reduce the shape.");
				_points.Clear();
				return;
			}

			int requests = 0;
			int matchedInstances = 0;
			ResolvedPrefabPlacement batchMetadata = ResolvePlacementMetadata(prefab);
			PieceRepeaterDiagnostics.Log("BatchBegin", "prefab=" + _prefabId + " mode=" + _mode
				+ " waypoints=" + _points.Count + " placements=" + _placements.Count
				+ " profile=" + batchMetadata.ProfileType + "/" + batchMetadata.Status + " axis=" + batchMetadata.Axis
				+ " joins=" + batchMetadata.JoinStart.ToString("F3") + "->" + batchMetadata.JoinEnd.ToString("F3")
				+ " positionOffset=" + batchMetadata.PositionOffset.ToString("F3")
				+ " yawOffset=" + batchMetadata.YawOffset.ToString("0.###") + " bottomOffset=" + batchMetadata.BottomOffset.ToString("0.###")
				+ " gap=" + _gapMeters.ToString("0.###") + " smoothing=" + PluginConfig.PieceRepeaterCornerRadius.Value.ToString("0.##"));
			IDisposable restoreIhPlacement = null;
			if (!anarchy && !InfinityHammerBridge.TrySuspendPlacementInterception(out restoreIhPlacement))
			{
				Notify(ToolDisplayName + ": could not safely bypass IH's active tool placement hook; no pieces placed.");
				_points.Clear();
				UpdatePreview();
				return;
			}

			if (!anarchy) WorldEditUndoBridge.BeginAction();
			using (ToolActionGate.BeginTrustedPlacement())
			{
				try
				{
					using (restoreIhPlacement)
					{
						for (int index = 0; index < _placements.Count; index++)
						{
							Placement placement = _placements[index];
							if (anarchy)
							{
								ZNetScene.instance.SpawnObject(placement.Position, placement.Rotation, prefab);
								requests++;
								PieceRepeaterDiagnostics.Log("SpawnRequest", "index=" + index + " prefab=" + _prefabId
									+ " position=" + placement.Position.ToString("F3")
									+ " yaw=" + placement.Rotation.eulerAngles.y.ToString("0.0") + " networkSpawn=true");
								continue;
							}

							HashSet<int> before = GetWearNTearInstanceIds();
							player.PlacePiece(piece, placement.Position, placement.Rotation, true);
							requests++;
							List<WearNTear> created = FindNewMatchingInstances(before, _prefabId, placement);
							if (created.Count > 0) matchedInstances++;
							float previousJoinError = index > 0 ? GetJoinError(_placements[index - 1], placement, prefab) : 0f;
							PieceRepeaterDiagnostics.Log("PlaceRequest", "index=" + index + " prefab=" + _prefabId
								+ " root=" + placement.Position.ToString("F3") + " yaw=" + placement.Rotation.eulerAngles.y.ToString("0.0")
								+ " previousJoinError=" + previousJoinError.ToString("0.000") + "m"
								+ " matchingWearNTearCreated=" + created.Count
								+ (created.Count > 0 ? " instance=" + created[0].name + " id=" + created[0].GetInstanceID() : string.Empty));
							for (int createdIndex = 0; createdIndex < created.Count; createdIndex++)
								_placementVerifications.Add(new PlacementVerification
								{
									Instance = created[createdIndex].gameObject,
									PrefabId = _prefabId,
									RequestedPosition = placement.Position,
									RequestedRotation = placement.Rotation,
									VerifyAfter = Time.unscaledTime + 2f
								});
						}
					}
				}
				catch (Exception ex)
				{
					PieceRepeaterDiagnostics.Log("BatchException", "requests=" + requests + " error=" + ex);
					SonicWorldToolsPlugin.ModLogger?.LogWarning("[Line Tool] Placement stopped after " + requests + " requests: " + ex.Message);
				}
				finally
				{
					if (!anarchy) WorldEditUndoBridge.EndAction();
				}
			}

			PieceRepeaterDiagnostics.Log("BatchEnd", "prefab=" + _prefabId + " requests=" + requests
				+ " withImmediateInstance=" + matchedInstances + " pendingDelayedVerification=" + _placementVerifications.Count);
			// Short on purpose: the detail (requests, matched instances) is in the diagnostics log above.
			Notify(ToolDisplayName + ": placed " + requests + " " + _prefabId + (anarchy ? " (check they appear)" : " (undo reverses it)"));
			if (_traceRows.Count > 0) _traceAnchor = new List<Vector3>(_traceRows[_traceRows.Count - 1]);
			_traceRows.Clear();
			_points.Clear();
			_hasLockedStartPlacement = false;
			_hasLockedStartAnchor = false;
			_lockedStartAnchorIsSnap = false;
			UpdatePreview();
		}

		private ResolvedPrefabPlacement ResolvePlacementMetadata(GameObject prefab)
		{
			ResolvedPrefabPlacement metadata = PrefabPlacementMetadata.Resolve(_prefabId, prefab);
			if (!AnarchyModeActive || !prefab) return metadata;

			// Anarchy deliberately opts out of catalog join/profile assumptions. Treat
			// every networked prefab as a ground prop and use the configured spacing.
			metadata.Axis = PrefabRepeatAxis.Auto;
			metadata.Spacing = Mathf.Max(0.1f, PluginConfig.PieceRepeaterGroundPropSpacing.Value);
			metadata.CenterOffset = Vector3.zero;
			metadata.PositionOffset = Vector3.zero;
			metadata.JoinStartOffset = Vector3.zero;
			metadata.JoinEndOffset = Vector3.zero;
			metadata.YawOffset = 0f;
			metadata.TangentYawOffset = 0f;
			metadata.BottomOffset = PrefabPlacementMetadata.GetLocalRendererBounds(prefab).min.y;
			metadata.HasBottomOffsetOverride = true;
			metadata.ProfileType = RepeatProfileType.GroundProp;
			metadata.Status = RepeatProfileStatus.Ready;
			metadata.Enabled = true;
			metadata.SupportsX = true;
			metadata.SupportsZ = true;
			metadata.JoinStart = Vector3.zero;
			metadata.JoinEnd = Vector3.zero;
			metadata.MeasurementSource = "anarchy-fallback";
			metadata.Confidence = "Low";
			return metadata;
		}

		private void HandleSnapPointInput(Player player)
		{
			bool previous = ToolInput.WasButtonDown("TabLeft");
			bool next = ToolInput.WasButtonDown("TabRight");
			if (previous == next) return;
			if (!ZNetScene.instance || string.IsNullOrEmpty(_prefabId))
			{
				Notify("Select or sample a piece before cycling its snap points.");
				return;
			}

			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			Piece piece = prefab ? prefab.GetComponent<Piece>() : null;
			if (!piece)
			{
				Notify("The selected prefab has no building piece snap points.");
				return;
			}

			var snapPoints = new List<Transform>();
			piece.GetSnapPoints(snapPoints);
			if (snapPoints.Count == 0)
			{
				Notify("This piece has no snap points to cycle.");
				return;
			}

			int stateCount = snapPoints.Count + 1; // Auto plus each source snap point.
			int state = _snapPointIndex + 1;
			state = (state + (next ? 1 : -1) + stateCount) % stateCount;
			_snapPointIndex = state - 1;
			string label = _snapPointIndex < 0 ? Localization.instance.Localize("$msg_snapping_auto") : snapPoints[_snapPointIndex].name;
			Notify(Localization.instance.Localize("$msg_snapping " + label));
			UpdatePreview();
		}

		private bool TryGetInitialSnapPlacement(Player player, Vector3 aim, out Placement placement)
		{
			return TryGetInitialSnapPlacement(player, aim, out placement, out _, out _);
		}

		private bool TryGetInitialSnapPlacement(Player player, Vector3 aim, out Placement placement,
			out Vector3 targetAnchorWorld, out Vector3 sourceAnchorLocal)
		{
			placement = default(Placement);
			targetAnchorWorld = Vector3.zero;
			sourceAnchorLocal = Vector3.zero;
			if (!player || !ZNetScene.instance || string.IsNullOrEmpty(_prefabId)) return false;
			GameObject prefab = ZNetScene.instance.GetPrefab(_prefabId);
			if (!prefab || !EnsureSnapProbe(prefab)) return false;
			ResolvedPrefabPlacement metadata = ResolvePlacementMetadata(prefab);
			Placement initial = GetInitialPlacement(prefab, aim, metadata);
			_snapProbe.transform.SetPositionAndRotation(initial.Position, initial.Rotation);
			if (!InfinityHammerBridge.TryFindSnapPair(player, _snapProbe.transform, _snapPointIndex,
				out Transform sourceSnapPoint, out Transform targetSnapPoint))
			{ placement = initial; return false; }

			targetAnchorWorld = targetSnapPoint.position;
			sourceAnchorLocal = _snapProbe.transform.InverseTransformPoint(sourceSnapPoint.position);
			Vector3 snappedRoot = initial.Position + targetSnapPoint.position - sourceSnapPoint.position;
			if (metadata.ProfileType == RepeatProfileType.GroundProp)
				snappedRoot = ApplySurfaceHeightMode(snappedRoot, -metadata.BottomOffset, snappedRoot.y);
			snappedRoot += initial.Rotation * metadata.PositionOffset;
			placement = new Placement(snappedRoot, initial.Rotation);
			return true;
		}

		private Placement GetInitialPlacement(GameObject prefab, Vector3 aim, ResolvedPrefabPlacement metadata)
		{
			Quaternion rotation = GetInitialPlacementRotation(metadata).normalized;
			float groundOffset = -metadata.BottomOffset;
			Vector3 rootPosition = aim - rotation * metadata.CenterOffset;
			rootPosition.y = aim.y + groundOffset;
			if (EnsureSnapProbe(prefab) && InfinityHammerBridge.TryGetBuildSurface(Player.m_localPlayer,
				out Vector3 point, out Vector3 normal, out bool terrain))
			{
				Piece piece = _snapProbe.GetComponent<Piece>();
				var snaps = new List<Transform>();
				piece.GetSnapPoints(snaps);
				_snapProbe.transform.SetPositionAndRotation(point, rotation);
				if (_snapPointIndex >= 0 && _snapPointIndex < snaps.Count)
					rootPosition = point - (snaps[_snapPointIndex].position - point);
				else if (terrain)
				{
					rootPosition = point - rotation * metadata.CenterOffset;
					rootPosition.y = point.y + groundOffset;
				}
				else if (piece.m_clipEverything)
					rootPosition = point;
				else
				{
					// Vanilla offsets the collider surface onto the hit before searching snap pairs.
					_snapProbe.transform.position = point + normal * 50f;
					float nearest = float.PositiveInfinity;
					Vector3 closest = point;
					foreach (Collider collider in _snapProbe.GetComponentsInChildren<Collider>(true))
					{
						if (!collider.enabled || collider.isTrigger) continue;
						if (!(collider is BoxCollider) && !(collider is SphereCollider) && !(collider is CapsuleCollider)
							&& !(collider is MeshCollider mesh && mesh.convex)) continue;
						Vector3 candidate = Physics.ClosestPoint(point, collider, collider.transform.position, collider.transform.rotation.normalized);
						float distance = Vector3.Distance(candidate, point);
						// A failed query can return the input point. It cannot be a valid
						// contact for a collider deliberately moved 50 metres away.
						if (distance > 0.001f && !float.IsNaN(distance) && distance < nearest)
						{ nearest = distance; closest = candidate; }
					}
					rootPosition = float.IsInfinity(nearest) ? point : point + _snapProbe.transform.position - closest;
				}
			}
			if (metadata.ProfileType == RepeatProfileType.GroundProp)
				rootPosition = ApplySurfaceHeightMode(rootPosition, groundOffset, rootPosition.y);
			rootPosition += rotation * metadata.PositionOffset;
			return new Placement(rootPosition, rotation);
		}

		private Placement GetAnarchyInitialPlacement(Vector3 aim, ResolvedPrefabPlacement metadata)
		{
			Quaternion rotation = Quaternion.Euler(0f, _sampledYaw, 0f);
			Vector3 rootPosition = aim;
			rootPosition.y = aim.y - metadata.BottomOffset;
			rootPosition = ApplySurfaceHeightMode(rootPosition, -metadata.BottomOffset, rootPosition.y);
			return new Placement(rootPosition, rotation);
		}

		private bool EnsureSnapProbe(GameObject prefab)
		{
			if (_snapProbe && string.Equals(_snapProbePrefabId, _prefabId, StringComparison.OrdinalIgnoreCase))
				return true;
			ClearSnapProbe();
			_snapProbeContainer = new GameObject("SonicWorldTools_PieceRepeaterSnapProbe") { hideFlags = HideFlags.HideAndDontSave };
			_snapProbeContainer.SetActive(false);
			UnityEngine.Object.DontDestroyOnLoad(_snapProbeContainer);
			_snapProbe = UnityEngine.Object.Instantiate(prefab, _snapProbeContainer.transform, false);
			_snapProbe.name = "PieceRepeaterSnapProbe";
			foreach (Component component in _snapProbe.GetComponentsInChildren<Component>(true))
				if (!(component is Piece) && !(component is Transform) && !(component is Collider))
					UnityEngine.Object.Destroy(component);
			_snapProbe.SetActive(true);
			_snapProbePrefabId = _prefabId;
			return true;
		}

		private void ClearSnapProbe()
		{
			if (_snapProbeContainer) UnityEngine.Object.Destroy(_snapProbeContainer);
			_snapProbeContainer = null;
			_snapProbe = null;
			_snapProbePrefabId = null;
		}

		private Quaternion GetInitialPlacementRotation(ResolvedPrefabPlacement metadata)
		{
			Quaternion rotation = Quaternion.Euler(0f, _sampledYaw, 0f);
			if (metadata.ProfileType == RepeatProfileType.EdgeChain && metadata.Axis == PrefabRepeatAxis.X)
				rotation *= Quaternion.Euler(0f, -90f, 0f);
			rotation *= Quaternion.Euler(0f, metadata.YawOffset, 0f);
			return Quaternion.Euler(0f, _rotationSteps * RotationStepDegrees, 0f) * rotation;
		}

		private void UpdateLockedStartForDirection(GameObject prefab, ResolvedPrefabPlacement metadata, Vector3 target)
		{
			if (!_hasLockedStartPlacement || !_hasLockedStartAnchor || !prefab) return;
			Vector3 direction = target - _lockedStartAnchorWorld;
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f) return;
			direction.Normalize();

			Vector3 localAxis = metadata.JoinEnd - metadata.JoinStart;
			localAxis.y = 0f;
			if (localAxis.sqrMagnitude < 0.0001f) return;
			float outgoingAxisSign = PieceRepeaterPathMath.ResolveOutgoingAxisSign(
				_lockedStartAnchorLocal, metadata.JoinStart, metadata.JoinEnd, _lockedStartAnchorIsSnap);
			localAxis.Normalize();
			localAxis *= outgoingAxisSign;
			float desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			float localYaw = Mathf.Atan2(localAxis.x, localAxis.z) * Mathf.Rad2Deg;
			float wheelYaw = (_rotationSteps - _rotationStepsAtStart) * RotationStepDegrees;
			float yaw = desiredYaw - localYaw - metadata.YawOffset + wheelYaw;
			Quaternion rotation = Quaternion.Euler(0f, yaw, 0f)
				* Quaternion.Euler(0f, metadata.YawOffset, 0f);
			Vector3 root = _lockedStartAnchorWorld - rotation * _lockedStartAnchorLocal
				+ rotation * metadata.PositionOffset;
			_lockedStartPlacement = new Placement(root, rotation);
			_points[0] = root + rotation * metadata.CenterOffset;
		}

		private static HashSet<int> GetWearNTearInstanceIds()
		{
			var ids = new HashSet<int>();
			List<WearNTear> instances = WearNTear.GetAllInstances();
			if (instances == null) return ids;
			for (int i = 0; i < instances.Count; i++)
				if (instances[i]) ids.Add(instances[i].GetInstanceID());
			return ids;
		}

		private static List<WearNTear> FindNewMatchingInstances(HashSet<int> before, string prefabId, Placement requested)
		{
			var matches = new List<WearNTear>();
			List<WearNTear> instances = WearNTear.GetAllInstances();
			if (instances == null) return matches;
			for (int i = 0; i < instances.Count; i++)
			{
				WearNTear instance = instances[i];
				if (!instance || before.Contains(instance.GetInstanceID())) continue;
				string instanceName = instance.gameObject.name;
				if (instanceName.EndsWith("(Clone)", StringComparison.Ordinal))
					instanceName = instanceName.Substring(0, instanceName.Length - 7);
				if (!string.Equals(instanceName, prefabId, StringComparison.OrdinalIgnoreCase)) continue;
				if (Vector3.Distance(instance.transform.position, requested.Position) > 0.1f
				    || Quaternion.Angle(instance.transform.rotation, requested.Rotation) > 2f) continue;
				matches.Add(instance);
			}
			return matches;
		}

		private void VerifyRecentPlacements()
		{
			for (int i = _placementVerifications.Count - 1; i >= 0; i--)
			{
				PlacementVerification verification = _placementVerifications[i];
				if (Time.unscaledTime < verification.VerifyAfter) continue;
				GameObject instance = verification.Instance;
				if (!instance)
				{
					PieceRepeaterDiagnostics.Log("DelayedCheck", "prefab=" + verification.PrefabId + " aliveAfter2s=false");
					_placementVerifications.RemoveAt(i);
					continue;
				}

				WearNTear wear = instance.GetComponent<WearNTear>();
				string support = "n/a";
				if (wear)
				{
					try
					{
						object value = HarmonyLib.AccessTools.Method(typeof(WearNTear), "GetSupport")?.Invoke(wear, null);
						support = value is float measured ? measured.ToString("0.###") : "unavailable";
					}
					catch (Exception ex) { support = "error:" + ex.GetType().Name; }
				}
				PieceRepeaterDiagnostics.Log("DelayedCheck", "prefab=" + verification.PrefabId
					+ " aliveAfter2s=true instance=" + instance.name
					+ " requested=" + verification.RequestedPosition.ToString("F3")
					+ " actual=" + instance.transform.position.ToString("F3")
					+ " positionDelta=" + Vector3.Distance(verification.RequestedPosition, instance.transform.position).ToString("0.000")
					+ "m rotationDelta=" + Quaternion.Angle(verification.RequestedRotation, instance.transform.rotation).ToString("0.0")
					+ "deg support=" + support
					+ " health=" + (wear ? wear.GetHealthPercentage().ToString("0.###") : "n/a"));
				_placementVerifications.RemoveAt(i);
			}
		}

		private static float GetJoinError(Placement previous, Placement current, GameObject prefab)
		{
			ResolvedPrefabPlacement metadata = PrefabPlacementMetadata.Resolve(prefab.name, prefab);
			Vector3 delta = current.Position - previous.Position;
			delta.y = 0f;
			if (delta.sqrMagnitude < 0.0001f) return 0f;
			Vector3 previousAxis = previous.Rotation * (metadata.JoinEnd - metadata.JoinStart);
			Vector3 currentAxis = current.Rotation * (metadata.JoinEnd - metadata.JoinStart);
			previousAxis.y = currentAxis.y = 0f;
			Vector3 direction = delta.normalized;
			Vector3 exit = previous.Position + previous.Rotation
				* (Vector3.Dot(previousAxis, direction) >= 0f ? metadata.JoinEnd : metadata.JoinStart);
			Vector3 entry = current.Position + current.Rotation
				* (Vector3.Dot(currentAxis, direction) >= 0f ? metadata.JoinStart : metadata.JoinEnd);
			Vector3 error = entry - exit;
			error.y = 0f;
			return error.magnitude;
		}

		// When level height is active, optionally build a vertical column below every
		// solved path placement. Lower rows are ordered first so pieces have support
		// before the elevated route is requested from Valheim.
		private void ExpandPlacementsDownToTerrain(GameObject prefab)
		{
			if (!PluginConfig.PieceRepeaterFillDownward.Value || !IsSameHeightHeld()
			    || !prefab || _placements.Count == 0) return;

			Bounds bounds = PrefabPlacementMetadata.GetLocalRendererBounds(prefab);
			float pieceHeight = bounds.size.y;
			if (pieceHeight < MinimumSpacing || float.IsNaN(pieceHeight) || float.IsInfinity(pieceHeight)) return;

			var bases = new List<Placement>(_placements);
			var fillDepths = new List<float>(bases.Count);
			var fillCounts = new List<int>(bases.Count);
			int maximumLayers = 0;
			int totalCopies = 0;
			for (int i = 0; i < bases.Count; i++)
			{
				Placement placement = bases[i];
				if (!TryGetTerrainHeightForDownwardFill(placement.Position, out float terrainHeight))
				{
					fillDepths.Add(0f);
					fillCounts.Add(0);
					continue;
				}

				float depth = placement.Position.y + bounds.min.y - terrainHeight;
				if (depth <= 0.02f || depth > MaximumDownwardFillDepth)
				{
					fillDepths.Add(0f);
					fillCounts.Add(0);
					continue;
				}

				int copies = Mathf.CeilToInt(depth / pieceHeight);
				fillDepths.Add(depth);
				fillCounts.Add(copies);
				totalCopies += copies;
				maximumLayers = Mathf.Max(maximumLayers, copies);
			}

			// Avoid a partial column or a preview that cannot be placed atomically.
			if (bases.Count + totalCopies > MaximumPlacements)
			{
				if (PluginConfig.LineToolDiagnostics.Value)
					PieceRepeaterDiagnostics.Log("DownwardFillSkipped", "prefab=" + _prefabId
						+ " basePlacements=" + bases.Count + " downwardCopies=" + totalCopies
						+ " limit=" + MaximumPlacements);
				return;
			}

			var expanded = new List<Placement>(bases.Count + totalCopies);
			for (int layer = maximumLayers; layer >= 1; layer--)
			{
				for (int i = 0; i < bases.Count; i++)
				{
					if (fillCounts[i] < layer) continue;
					Placement lower = bases[i];
					float depth = fillDepths[i];
					float down = layer == fillCounts[i] ? depth : pieceHeight * layer;
					lower.Position.y -= down;
					expanded.Add(lower);
				}
			}
			expanded.AddRange(bases);
			_placements.Clear();
			_placements.AddRange(expanded);
		}

		private static bool TryGetTerrainHeightForDownwardFill(Vector3 position, out float terrainHeight)
		{
			terrainHeight = 0f;
			if (!ZoneSystem.instance) return false;
			// This bool API distinguishes a valid ground sample from GetSolidHeight's
			// zero fallback, which otherwise creates spurious columns toward world Y=0.
			return ZoneSystem.instance.GetGroundHeight(position, out terrainHeight)
			       && !float.IsNaN(terrainHeight)
			       && !float.IsInfinity(terrainHeight)
			       && position.y - terrainHeight <= MaximumDownwardFillDepth + 0.02f;
		}

		private Vector3 ApplyHeightMode(Vector3 position, float groundOffset, float fixedHeight)
		{
			if (IsSameHeightHeld() && _points.Count > 0)
			{
				if (TryGetGroundHeight(_points[0], out float firstGroundHeight))
				{
					position.y = firstGroundHeight + groundOffset;
				}
				else position.y = fixedHeight + groundOffset;
			}
			else if (TryGetGroundHeight(position, out float groundHeight))
			{
				position.y = groundHeight + groundOffset;
			}
			else position.y += groundOffset;
			return position;
		}

		private Vector3 ApplySurfaceHeightMode(Vector3 position, float groundOffset, float fixedHeight)
		{
			// Level mode preserves the established route height. Otherwise, use the
			// highest build-piece surface under the profile's footprint, then terrain.
			if (IsSameHeightHeld() && _points.Count > 0)
			{
				position.y = fixedHeight;
				return position;
			}

			if (TryGetBuildPieceSurfaceHeight(position, groundOffset, out float pieceHeight))
				position.y = pieceHeight + groundOffset;
			else if (TryGetGroundHeight(position, out float groundHeight))
				position.y = groundHeight + groundOffset;
			else
				position.y += groundOffset;
			return position;
		}

		private bool TryGetBuildPieceSurfaceHeight(Vector3 position, float groundOffset, out float surfaceHeight)
		{
			surfaceHeight = 0f;
			float rayStartY = position.y + Mathf.Max(2f, groundOffset + 1f);
			float rayDistance = Mathf.Max(12f, groundOffset + 10f);
			Vector3 origin = new Vector3(position.x, rayStartY, position.z);
			int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _surfaceRayHits,
				rayDistance, ~0, QueryTriggerInteraction.Ignore);
			float highestSupport = float.NegativeInfinity;
			Player localPlayer = Player.m_localPlayer;
			for (int i = 0; i < hitCount; i++)
			{
				Collider collider = _surfaceRayHits[i].collider;
				if (!collider || collider.isTrigger) continue;
				Transform hitTransform = collider.transform;
				if ((_previewRoot && hitTransform.IsChildOf(_previewRoot.transform))
				    || (_snapProbeContainer && hitTransform.IsChildOf(_snapProbeContainer.transform))
				    || (localPlayer && hitTransform.IsChildOf(localPlayer.transform)))
					continue;

				Piece piece = collider.GetComponentInParent<Piece>();
				if (!piece) continue;
				float hitY = _surfaceRayHits[i].point.y;
				// The ray begins above the intended contact height. Ignore overhead
				// pieces, and do not snap down to a distant lower floor by accident.
				if (hitY > position.y + 0.1f || hitY < position.y - rayDistance + 1f) continue;
				if (hitY > highestSupport) highestSupport = hitY;
			}

			if (float.IsNegativeInfinity(highestSupport)) return false;
			surfaceHeight = highestSupport;
			return true;
		}

		private static bool TryGetGroundHeight(Vector3 position, out float groundHeight)
		{
			groundHeight = 0f;
			if (!ZoneSystem.instance) return false;
			ZoneSystem.instance.GetSolidHeight(position, out groundHeight, 0);
			// GetSolidHeight can return its zero default when the requested column has no
			// terrain sample. A 40 m tolerance accepted that sentinel on ordinary hills
			// and sent some pieces to y=0.5 while adjacent pieces stayed at terrain height.
			// Waypoints are aimed at visible surfaces, so a sample farther than 8 m from
			// the requested height is treated as unavailable and the path height is kept.
			return !float.IsNaN(groundHeight)
			       && !float.IsInfinity(groundHeight)
			       && Mathf.Abs(groundHeight - position.y) <= MaximumTerrainSampleDelta;
		}

		private bool IsSameHeightHeld()
		{
			// The F7 height setting is the default; holding the level modifier (Left Alt) flips it either way.
			// Applies to the Line Tool, Shape Tool and traced rows alike.
			return PluginConfig.PieceRepeaterLevelByDefault.Value
				!= ToolInput.WasModifierHeld(PluginConfig.PieceRepeaterLevelModifier.Value);
		}
	}
}
