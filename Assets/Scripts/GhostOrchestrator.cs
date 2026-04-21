using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace WorkspaceGhostPreview
{
    /// <summary>
    /// Converts 2D detections plus Ollama offsets into ghost previews and
    /// attaches confirmation watchers to each spawned ghost.
    /// </summary>
    public sealed class GhostOrchestrator : MonoBehaviour
    {
        [Serializable]
        private sealed class GhostPlacementResponse
        {
            public string @object = string.Empty;
            public float[] targetOffset = Array.Empty<float>();
        }

        [Serializable]
        public sealed class GhostObjectEvent : UnityEvent<GameObject>
        {
        }

        [Serializable]
        public sealed class GhostStringEvent : UnityEvent<string>
        {
        }

        [Header("Dependencies")]
        [SerializeField] private GhostManager ghostManager;
        [SerializeField] private DepthRaycaster depthRaycaster;

        [Header("Events")]
        [SerializeField] private GhostObjectEvent onGhostSpawned = new GhostObjectEvent();
        [SerializeField] private GhostObjectEvent onGhostConfirmed = new GhostObjectEvent();
        [SerializeField] private GhostStringEvent onGhostError = new GhostStringEvent();

        private readonly Dictionary<GameObject, GhostConfirmation> confirmationsByGhost =
            new Dictionary<GameObject, GhostConfirmation>();

        public GhostObjectEvent OnGhostSpawned => onGhostSpawned;
        public GhostObjectEvent OnGhostConfirmed => onGhostConfirmed;
        public GhostStringEvent OnGhostError => onGhostError;

        private void Awake()
        {
            ResolveReferences();
        }

        public GameObject SpawnGhostFromResponse(string detectedLabel, Vector2 screenPoint, string ollamaJsonResponse)
        {
            if (!TryParsePlacementResponse(ollamaJsonResponse, out GhostPlacementResponse response, out string error))
            {
                ReportError(error);
                return null;
            }

            Vector3 targetOffset = ToVector3(response.targetOffset);
            return SpawnGhostFromOffset(detectedLabel, screenPoint, targetOffset);
        }

        public GameObject SpawnGhostFromOffset(string detectedLabel, Vector2 screenPoint, Vector3 targetOffset)
        {
            ResolveReferences();

            if (ghostManager == null)
            {
                ReportError("GhostManager is not assigned.");
                return null;
            }

            if (depthRaycaster == null)
            {
                ReportError("DepthRaycaster is not assigned.");
                return null;
            }

            Vector3 detectedWorldPosition = depthRaycaster.ResolveWorldPoint(screenPoint);
            Vector3 targetWorldPosition = detectedWorldPosition + targetOffset;

            GameObject ghost = ghostManager.SpawnGhostAndReturn(detectedLabel, targetWorldPosition);
            if (ghost == null)
            {
                ReportError("GhostManager failed to create a ghost preview.");
                return null;
            }

            GhostConfirmation confirmation = ghost.GetComponent<GhostConfirmation>();
            if (confirmation == null)
            {
                confirmation = ghost.AddComponent<GhostConfirmation>();
            }

            confirmation.GhostObject = ghost;
            confirmation.trackedWorldPosition = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            confirmation.ResetConfirmationCooldown();
            confirmation.OnConfirmed.AddListener(() => HandleGhostConfirmed(ghost));

            confirmationsByGhost[ghost] = confirmation;
            onGhostSpawned.Invoke(ghost);
            return ghost;
        }

        public void UpdateTrackedPosition(GameObject ghost, Vector3 trackedWorldPosition)
        {
            if (ghost == null)
            {
                return;
            }

            if (confirmationsByGhost.TryGetValue(ghost, out GhostConfirmation confirmation) && confirmation != null)
            {
                confirmation.trackedWorldPosition = trackedWorldPosition;
                return;
            }

            confirmation = ghost.GetComponent<GhostConfirmation>();
            if (confirmation != null)
            {
                confirmation.trackedWorldPosition = trackedWorldPosition;
                confirmationsByGhost[ghost] = confirmation;
            }
        }

        public void UpdateTrackedPositionByLabel(string label, Vector3 trackedWorldPosition)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            List<GameObject> missingGhosts = null;
            foreach (KeyValuePair<GameObject, GhostConfirmation> pair in confirmationsByGhost)
            {
                GameObject ghost = pair.Key;
                GhostConfirmation confirmation = pair.Value;

                if (ghost == null || confirmation == null)
                {
                    if (missingGhosts == null)
                    {
                        missingGhosts = new List<GameObject>();
                    }

                    missingGhosts.Add(ghost);
                    continue;
                }

                if (ghost.name.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    confirmation.trackedWorldPosition = trackedWorldPosition;
                }
            }

            if (missingGhosts == null)
            {
                return;
            }

            for (int i = 0; i < missingGhosts.Count; i++)
            {
                confirmationsByGhost.Remove(missingGhosts[i]);
            }
        }

        public void ClearAllGhosts()
        {
            confirmationsByGhost.Clear();

            if (ghostManager != null)
            {
                ghostManager.ClearAllGhosts();
            }
        }

        private void ResolveReferences()
        {
            if (ghostManager == null)
            {
                ghostManager = GetComponent<GhostManager>();
            }

            if (depthRaycaster == null)
            {
                depthRaycaster = GetComponent<DepthRaycaster>();
            }

            if (ghostManager == null)
            {
                ghostManager = FindAnyObjectByType<GhostManager>();
            }

            if (depthRaycaster == null)
            {
                depthRaycaster = FindAnyObjectByType<DepthRaycaster>();
            }
        }

        private void HandleGhostConfirmed(GameObject ghost)
        {
            if (ghost != null)
            {
                confirmationsByGhost.Remove(ghost);
            }

            onGhostConfirmed.Invoke(ghost);
        }

        private void ReportError(string message)
        {
            Debug.LogWarning("[GhostOrchestrator] " + message, this);
            onGhostError.Invoke(message);
        }

        private static bool TryParsePlacementResponse(
            string rawJson,
            out GhostPlacementResponse response,
            out string error)
        {
            response = null;

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                error = "Ollama placement JSON was empty.";
                return false;
            }

            try
            {
                response = JsonUtility.FromJson<GhostPlacementResponse>(rawJson);
            }
            catch (ArgumentException exception)
            {
                error = "Invalid Ollama placement JSON: " + exception.Message;
                return false;
            }

            if (response == null)
            {
                error = "Invalid Ollama placement JSON.";
                return false;
            }

            if (response.targetOffset == null || response.targetOffset.Length < 3)
            {
                error = "Ollama placement JSON is missing a 3-value targetOffset array.";
                return false;
            }

            error = null;
            return true;
        }

        private static Vector3 ToVector3(float[] values)
        {
            if (values == null || values.Length < 3)
            {
                return Vector3.zero;
            }

            return new Vector3(values[0], values[1], values[2]);
        }
    }
}
