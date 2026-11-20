using System;
using UnityEngine;

namespace SonicWorldTools.Rendering
{
	/// <summary>Builds render-only translucent copies of a prefab for placement previews.</summary>
	internal static class PrefabGhostFactory
	{
		/// <summary>
		/// Copies the prefab's active children under a new inactive-then-active root, strips every
		/// component except renderers/mesh filters/transforms (no colliders, no ZNetView, no scripts),
		/// disables particles and the "largelod" impostor, and swaps all materials for <paramref name="material"/>.
		/// </summary>
		internal static GameObject Create(GameObject prefab, Transform parent, Material material, string name)
		{
			var ghost = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
			ghost.transform.SetParent(parent, false);
			ghost.SetActive(false);
			int pieceLayer = LayerMask.NameToLayer("piece");
			foreach (Transform child in prefab.transform)
			{
				if (!child.gameObject.activeSelf) continue;
				GameObject visual = UnityEngine.Object.Instantiate(child.gameObject, ghost.transform);
				visual.transform.localPosition = child.localPosition;
				visual.transform.localRotation = child.localRotation;
				visual.transform.localScale = child.localScale;
				SetLayerRecursively(visual.transform, pieceLayer);
			}

			// DestroyImmediate: a deferred Destroy leaves components such as CharacterAnimEvent alive when the
			// ghost activates below, and their Awake then fails on the stripped references.
			foreach (Component component in ghost.GetComponentsInChildren<Component>(true))
				if (!(component is Renderer) && !(component is MeshFilter) && !(component is Transform))
					UnityEngine.Object.DestroyImmediate(component);

			foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer is ParticleSystemRenderer || string.Equals(renderer.name, "largelod", StringComparison.OrdinalIgnoreCase))
				{
					renderer.enabled = false;
					continue;
				}
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				Material[] materials = renderer.sharedMaterials;
				for (int i = 0; i < materials.Length; i++) materials[i] = material;
				renderer.sharedMaterials = materials;
			}
			ghost.SetActive(true);
			return ghost;
		}

		internal static void SetLayerRecursively(Transform transform, int layer)
		{
			if (layer >= 0) transform.gameObject.layer = layer;
			for (int i = 0; i < transform.childCount; i++) SetLayerRecursively(transform.GetChild(i), layer);
		}
	}
}
