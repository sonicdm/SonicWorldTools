using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Utils;
using System.Linq;
using UnityEngine;

namespace SonicWorldTools.Integration
{
	/// <summary>Replaces only SonicWorldTools terrain tool icons; vanilla IH tools remain untouched.</summary>
	internal static class TerrainToolIconOverrides
	{
		private const string AtlasResource = "SonicWorldTools.terrain-surface-atlas.png";
		private static readonly string[] SurfaceToolNames =
		{
			"Grass Path", "Grass Patches Path", "Dark Grass Path", "Dirt Path",
			"Cultivated Path", "Paved Path", "Mossy Paved Path", "Dark Paved Path"
		};
		private static Sprite[] _surfaceSprites;
		private static Sprite _lowerSprite;
		private static bool _lowerIconAttempted;
		private static bool _loadFailed;
		private static bool _ihPatchInstalled;
		private static readonly HashSet<string> LoggedOverrides = new HashSet<string>(StringComparer.Ordinal);

		internal static void TryInstall(Harmony harmony)
		{
			if (_ihPatchInstalled || harmony == null)
			{
				return;
			}

			try
			{
				Assembly ihAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
					string.Equals(assembly.GetName().Name, "InfinityHammer", StringComparison.OrdinalIgnoreCase));
				Type toolMenuPieces = ihAssembly?.GetType("InfinityHammer.ToolMenuPieces", false);
				Type toolType = ihAssembly?.GetType("InfinityHammer.Tool", false);
				if (toolMenuPieces == null || toolType == null)
				{
					return;
				}

				MethodInfo getToolPiece = toolMenuPieces.GetMethod(
					"Get",
					BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
					null,
					new[] { typeof(PieceTable), toolType },
					null);
				if (getToolPiece == null)
				{
					SonicWorldToolsPlugin.ModLogger?.LogWarning("Could not find Infinity Hammer's tool icon creation method.");
					return;
				}

				harmony.Patch(getToolPiece, postfix: new HarmonyMethod(typeof(TerrainToolIconOverrides), nameof(AfterToolPieceCreated)));
				_ihPatchInstalled = true;
				SonicWorldToolsPlugin.ModLogger?.LogInfo("Installed Infinity Hammer terrain icon override.");
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("Could not install Infinity Hammer terrain icon override: " + ex.Message);
			}
		}

		private static void AfterToolPieceCreated(object __result)
		{
			Piece piece = __result as Piece;
			if (piece == null)
			{
				return;
			}

			EnsureSprites();
			string objectName = piece.gameObject.name;
			if (objectName == "_IH_Tool_Lower Terrain")
			{
				if (_lowerSprite != null)
				{
					piece.m_icon = _lowerSprite;
					LogOverride(objectName);
				}
				return;
			}

			if (_surfaceSprites == null)
			{
				return;
			}

			for (int surface = 0; surface < SurfaceToolNames.Length; surface++)
			{
				if (objectName == "_IH_Tool_" + SurfaceToolNames[surface])
				{
					piece.m_icon = _surfaceSprites[surface];
					LogOverride(objectName);
					return;
				}
			}
		}

		private static void LogOverride(string objectName)
		{
			if (LoggedOverrides.Add(objectName))
			{
				SonicWorldToolsPlugin.ModLogger?.LogInfo("Applied custom icon to " + objectName + ".");
			}
		}

		private static void EnsureSprites()
		{
			if (_surfaceSprites == null && !_loadFailed)
			{
				try
				{
					using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AtlasResource))
					{
						if (stream == null)
						{
							throw new InvalidOperationException("Embedded terrain icon atlas is missing.");
						}

						byte[] bytes = new byte[stream.Length];
						stream.Read(bytes, 0, bytes.Length);
						Texture2D atlas = AssetUtils.LoadImage(bytes);
						if (atlas == null)
						{
							throw new InvalidOperationException("Terrain icon atlas could not be decoded.");
						}

						atlas.name = "SonicWorldToolsTerrainIconAtlas";
						atlas.filterMode = FilterMode.Bilinear;
						atlas.wrapMode = TextureWrapMode.Clamp;
						int cellWidth = atlas.width / 3;
						int cellHeight = atlas.height / 3;
						_surfaceSprites = new Sprite[SurfaceToolNames.Length];
						for (int index = 0; index < _surfaceSprites.Length; index++)
						{
							int row = index / 3;
							int column = index % 3;
							Rect cell = new Rect(column * cellWidth, atlas.height - (row + 1) * cellHeight, cellWidth, cellHeight);
							_surfaceSprites[index] = Sprite.Create(atlas, cell, new Vector2(0.5f, 0.5f), cellWidth);
							_surfaceSprites[index].name = "SonicWorldTools_" + SurfaceToolNames[index].Replace(" ", "_");
						}
					}
				}
				catch (Exception ex)
				{
					_loadFailed = true;
					SonicWorldToolsPlugin.ModLogger?.LogWarning("Terrain tool icon override unavailable: " + ex.Message);
				}
			}

			if (_lowerSprite == null && !_lowerIconAttempted)
			{
				Sprite raiseIcon = FindRaiseIcon();
				if (raiseIcon != null)
				{
					_lowerIconAttempted = true;
					_lowerSprite = Rotate180(raiseIcon);
				}
			}
		}

		private static Sprite FindRaiseIcon()
		{
			if (ZNetScene.instance != null)
			{
				GameObject prefab = ZNetScene.instance.GetPrefab("raise".GetStableHashCode());
				Sprite icon = null;
				if (prefab != null)
				{
					Piece piece = prefab.GetComponent<Piece>();
					if (piece != null)
					{
						icon = piece.m_icon;
					}
				}

				if (icon == null)
				{
					ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
					if (itemDrop != null && itemDrop.m_itemData != null && itemDrop.m_itemData.m_shared != null)
					{
						icon = itemDrop.m_itemData.m_shared.m_icons.FirstOrDefault();
					}
				}
				if (icon != null)
				{
					return icon;
				}
			}

			if (ObjectDB.instance != null && ObjectDB.instance.m_StatusEffects != null)
			{
				StatusEffect effect = ObjectDB.instance.m_StatusEffects.FirstOrDefault(candidate =>
					string.Equals(candidate.name, "raise", StringComparison.OrdinalIgnoreCase));
				if (effect != null)
				{
					return effect.m_icon;
				}
			}

			return null;
		}

		private static Sprite Rotate180(Sprite source)
		{
			try
			{
				Rect rect = source.textureRect;
				int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
				int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
				RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
				RenderTexture previous = RenderTexture.active;
				Graphics.Blit(
					source.texture,
					renderTexture,
					new Vector2(-rect.width / source.texture.width, -rect.height / source.texture.height),
					new Vector2(rect.xMax / source.texture.width, rect.yMax / source.texture.height));
				RenderTexture.active = renderTexture;
				Texture2D rotatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
				rotatedTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				rotatedTexture.Apply(false, false);
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(renderTexture);
				rotatedTexture.name = "SonicWorldToolsLowerTerrainIcon";
				return Sprite.Create(rotatedTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
			}
			catch (Exception ex)
			{
				SonicWorldToolsPlugin.ModLogger?.LogWarning("Could not rotate IH Raise icon: " + ex.Message);
				return null;
			}
		}
	}
}
