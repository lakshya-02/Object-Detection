using System;
using UnityEngine;
using UnityEngine.Events;

namespace WorkspaceGhostPreview
{
    /// <summary>
    /// Watches a tracked real-world position and confirms a ghost once the
    /// tracked point reaches the ghost target.
    /// </summary>
    public sealed class GhostConfirmation : MonoBehaviour
    {
        [SerializeField] private GameObject ghostObject;
        [SerializeField] private float confirmationDistance = 0.12f;
        [SerializeField] private float confirmationDelaySeconds = 1f;
        [SerializeField] private UnityEvent onConfirmed = new UnityEvent();

        [NonSerialized] public Vector3 trackedWorldPosition = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

        private float confirmationStartTime;
        private bool isConfirmed;

        public UnityEvent OnConfirmed => onConfirmed;

        public GameObject GhostObject
        {
            get => ghostObject;
            set => ghostObject = value;
        }

        private void OnEnable()
        {
            confirmationStartTime = Time.unscaledTime + confirmationDelaySeconds;
        }

        private void Reset()
        {
            ghostObject = gameObject;
        }

        private void Update()
        {
            if (isConfirmed || ghostObject == null)
            {
                return;
            }

            if (Time.unscaledTime < confirmationStartTime)
            {
                return;
            }

            if (!IsFinite(trackedWorldPosition))
            {
                return;
            }

            float distance = Vector3.Distance(trackedWorldPosition, ghostObject.transform.position);
            if (distance >= confirmationDistance)
            {
                return;
            }

            isConfirmed = true;
            onConfirmed.Invoke();
            Destroy(ghostObject);
        }

        public void SetTrackedWorldPosition(Vector3 worldPosition)
        {
            trackedWorldPosition = worldPosition;
        }

        public void ResetConfirmationCooldown()
        {
            confirmationStartTime = Time.unscaledTime + confirmationDelaySeconds;
            isConfirmed = false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
