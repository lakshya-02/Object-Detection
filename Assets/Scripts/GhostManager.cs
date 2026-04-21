using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkspaceGhostPreview
{
    /// <summary>
    /// Spawns and animates simple translucent ghost previews at target positions.
    /// </summary>
    public sealed class GhostManager : MonoBehaviour
    {
        [Serializable]
        private sealed class GhostEntry
        {
            public GameObject ghostObject;
            public Renderer renderer;
            public Material materialInstance;
            public float phaseOffset;
        }

        [Header("Ghost Visual")]
        [SerializeField] private Vector3 placeholderScale = new Vector3(0.3f, 0.15f, 0.2f);
        [SerializeField] private Color ghostColor = new Color(0.35f, 0.9f, 1f, 0.4f);
        [SerializeField, Range(0f, 1f)] private float defaultAlpha = 0.4f;
        [SerializeField, Range(0f, 1f)] private float pulseMinAlpha = 0.3f;
        [SerializeField, Range(0f, 1f)] private float pulseMaxAlpha = 0.5f;
        [SerializeField, Min(0.1f)] private float pulseSpeed = 2f;
        [SerializeField] private string ghostContainerName = "GhostPreviewContainer";
        [SerializeField] private Transform ghostContainer;

        private readonly List<GhostEntry> activeGhosts = new List<GhostEntry>();

        public GameObject LastSpawnedGhost { get; private set; }

        private void Update()
        {
            for (int i = activeGhosts.Count - 1; i >= 0; i--)
            {
                GhostEntry entry = activeGhosts[i];
                if (entry == null || entry.ghostObject == null || entry.materialInstance == null)
                {
                    CleanupGhostEntry(entry);
                    activeGhosts.RemoveAt(i);
                    continue;
                }

                float t = (Mathf.Sin((Time.unscaledTime + entry.phaseOffset) * pulseSpeed) + 1f) * 0.5f;
                float alpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t);
                SetMaterialAlpha(entry.materialInstance, alpha);
            }
        }

        public void SpawnGhost(string label, Vector3 targetWorldPos)
        {
            LastSpawnedGhost = CreateGhost(label, targetWorldPos);
        }

        public GameObject SpawnGhostAndReturn(string label, Vector3 targetWorldPos)
        {
            SpawnGhost(label, targetWorldPos);
            return LastSpawnedGhost;
        }

        public void ClearAllGhosts()
        {
            for (int i = activeGhosts.Count - 1; i >= 0; i--)
            {
                GhostEntry entry = activeGhosts[i];
                if (entry != null && entry.ghostObject != null)
                {
                    Destroy(entry.ghostObject);
                }

                CleanupGhostEntry(entry);
            }

            activeGhosts.Clear();
            LastSpawnedGhost = null;
        }

        private GameObject CreateGhost(string label, Vector3 targetWorldPos)
        {
            EnsureGhostContainer();

            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = string.IsNullOrWhiteSpace(label) ? "GhostPreview" : "GhostPreview_" + label.Trim();
            ghost.transform.SetParent(ghostContainer, false);
            ghost.transform.position = targetWorldPos;
            ghost.transform.rotation = Quaternion.identity;
            ghost.transform.localScale = placeholderScale;

            Collider ghostCollider = ghost.GetComponent<Collider>();
            if (ghostCollider != null)
            {
                Destroy(ghostCollider);
            }

            Renderer renderer = ghost.GetComponent<Renderer>();
            Material ghostMaterial = BuildGhostMaterial();
            renderer.sharedMaterial = ghostMaterial;
            SetMaterialAlpha(ghostMaterial, defaultAlpha);

            activeGhosts.Add(new GhostEntry
            {
                ghostObject = ghost,
                renderer = renderer,
                materialInstance = ghostMaterial,
                phaseOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
            });

            return ghost;
        }

        private void EnsureGhostContainer()
        {
            if (ghostContainer != null)
            {
                return;
            }

            Transform existingContainer = transform.Find(ghostContainerName);
            if (existingContainer != null)
            {
                ghostContainer = existingContainer;
                return;
            }

            GameObject containerObject = new GameObject(ghostContainerName);
            containerObject.transform.SetParent(transform, false);
            ghostContainer = containerObject.transform;
        }

        private Material BuildGhostMaterial()
        {
            Shader standardShader = Shader.Find("Standard");
            Material material = standardShader != null
                ? new Material(standardShader)
                : new Material(Shader.Find("Sprites/Default"));

            material.name = "GhostPreviewMaterial_Instance";
            ConfigureForTransparency(material);
            material.color = new Color(ghostColor.r, ghostColor.g, ghostColor.b, defaultAlpha);
            return material;
        }

        private static void ConfigureForTransparency(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private void SetMaterialAlpha(Material material, float alpha)
        {
            if (material == null)
            {
                return;
            }

            Color color = material.color;
            color.r = ghostColor.r;
            color.g = ghostColor.g;
            color.b = ghostColor.b;
            color.a = Mathf.Clamp01(alpha);
            material.color = color;
        }

        private static void CleanupGhostEntry(GhostEntry entry)
        {
            if (entry != null && entry.materialInstance != null)
            {
                Destroy(entry.materialInstance);
            }
        }
    }
}
