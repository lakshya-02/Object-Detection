using UnityEngine;

namespace WorkspaceGhostPreview
{
    /// <summary>
    /// Resolves a 2D screen-space detection point into a world-space point by
    /// raycasting from the Quest center eye.
    /// </summary>
    public sealed class DepthRaycaster : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private OVRCameraRig ovrCameraRig;
        [SerializeField] private Camera overrideCamera;

        [Header("Raycast")]
        [SerializeField, Min(0.1f)] private float maxRayDistance = 8f;
        [SerializeField, Min(0.05f)] private float fallbackDepth = 0.6f;
        [SerializeField] private bool preferRealWorldLayer = true;

        [Tooltip("Additional layers to include with the RealWorld layer when present.")]
        [SerializeField] private LayerMask additionalLayerMask;

        public Vector3 ResolveWorldPoint(Vector2 screenPoint)
        {
            TryResolveWorldPoint(screenPoint, out Vector3 worldPoint, out _);
            return worldPoint;
        }

        public bool TryResolveWorldPoint(Vector2 screenPoint, out Vector3 worldPoint, out RaycastHit hitInfo)
        {
            Camera rayCamera = GetRaycastCamera();
            if (rayCamera == null)
            {
                worldPoint = transform.position + transform.forward * fallbackDepth;
                hitInfo = default;
                return false;
            }

            Ray ray = rayCamera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));
            LayerMask preferredMask = GetPreferredLayerMask();

            if (preferredMask.value != 0 &&
                Physics.Raycast(ray, out hitInfo, maxRayDistance, preferredMask, QueryTriggerInteraction.Ignore))
            {
                worldPoint = hitInfo.point;
                return true;
            }

            if (Physics.Raycast(ray, out hitInfo, maxRayDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                worldPoint = hitInfo.point;
                return true;
            }

            worldPoint = rayCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, fallbackDepth));
            hitInfo = default;
            return false;
        }

        private Camera GetRaycastCamera()
        {
            if (overrideCamera != null)
            {
                return overrideCamera;
            }

            if (ovrCameraRig == null)
            {
                ovrCameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (ovrCameraRig != null && ovrCameraRig.centerEyeAnchor != null)
            {
                Camera centerEyeCamera = ovrCameraRig.centerEyeAnchor.GetComponent<Camera>();
                if (centerEyeCamera != null)
                {
                    return centerEyeCamera;
                }
            }

            return Camera.main;
        }

        private LayerMask GetPreferredLayerMask()
        {
            LayerMask mask = additionalLayerMask;
            if (!preferRealWorldLayer)
            {
                return mask;
            }

            int realWorldLayer = LayerMask.NameToLayer("RealWorld");
            if (realWorldLayer >= 0)
            {
                mask |= 1 << realWorldLayer;
            }

            return mask;
        }
    }
}
