using UnityEngine;

namespace CustomNavigation.Runtime
{
    /// <summary>
    /// Isometric camera for the demo scenes: frames the whole level, keeps the viewport
    /// clear of the on-screen GUI and follows device safe areas.
    ///
    /// This class must stay in a file named exactly like the type. Unity only creates a
    /// MonoScript for the class whose name matches the file, so a MonoBehaviour declared
    /// in a differently named file cannot be instantiated - AddComponent returns null and
    /// logs "The referenced script (Unknown) on this Behaviour is missing!".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NavigationDemoIsometricCameraRig : MonoBehaviour
    {
        private Camera worldCamera;
        private Bounds worldBounds;
        private float pitch;
        private float yaw;
        private float padding;
        private int fittedWidth = -1;
        private int fittedHeight = -1;
        private Rect fittedSafeArea;

        public Camera WorldCamera => worldCamera;

        public static NavigationDemoIsometricCameraRig Create(
            Transform parent,
            string objectName,
            Bounds bounds,
            Color backgroundColor,
            float pitch = 52f,
            float yaw = 45f,
            float padding = 1.25f)
        {
            var cameraObject = new GameObject(objectName);
            cameraObject.transform.SetParent(parent, false);
            var rig = cameraObject.AddComponent<NavigationDemoIsometricCameraRig>();
            rig.worldBounds = bounds;
            rig.pitch = pitch;
            rig.yaw = yaw;
            rig.padding = padding;

            var backgroundObject = new GameObject(objectName + " background");
            backgroundObject.transform.SetParent(cameraObject.transform, false);
            var backgroundCamera = backgroundObject.AddComponent<Camera>();
            backgroundCamera.orthographic = true;
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            backgroundCamera.backgroundColor = backgroundColor;
            backgroundCamera.cullingMask = 0;
            backgroundCamera.depth = -100f;

            rig.worldCamera = cameraObject.AddComponent<Camera>();
            rig.worldCamera.orthographic = true;
            rig.worldCamera.clearFlags = CameraClearFlags.SolidColor;
            rig.worldCamera.backgroundColor = backgroundColor;
            rig.worldCamera.nearClipPlane = 0.1f;
            rig.worldCamera.farClipPlane = 200f;
            rig.worldCamera.depth = 0f;
            rig.FitCamera(true);
            return rig;
        }

        public void SetWorldBounds(Bounds bounds)
        {
            worldBounds = bounds;
            FitCamera(true);
        }

        private void LateUpdate()
        {
            FitCamera(false);
        }

        private void FitCamera(bool force)
        {
            if (worldCamera == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Rect safeArea = NavigationDemoPresentation.GetSafeArea();
            if (!force
                && fittedWidth == Screen.width
                && fittedHeight == Screen.height
                && fittedSafeArea == safeArea)
            {
                return;
            }

            float scale = NavigationDemoPresentation.CalculateGuiScale(safeArea);
            float edge = NavigationDemoPresentation.EdgeMargin * scale;
            float availableHeight = Mathf.Max(64f, safeArea.height - edge * 2f);
            float header = Mathf.Min(
                NavigationDemoPresentation.HeaderHeight * scale,
                availableHeight * 0.55f);
            float footer = Mathf.Min(
                NavigationDemoPresentation.FooterHeight * scale,
                availableHeight * 0.2f);
            float viewWidth = Mathf.Max(64f, safeArea.width - edge * 2f);
            float viewHeight = Mathf.Max(64f, safeArea.height - edge * 2f - header - footer);
            worldCamera.pixelRect = new Rect(
                safeArea.x + edge,
                safeArea.y + edge + footer,
                viewWidth,
                viewHeight);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            worldCamera.transform.rotation = rotation;
            Vector3 focus = worldBounds.center;
            float distance = Mathf.Max(40f, worldBounds.extents.magnitude * 3f);
            worldCamera.transform.position = focus - rotation * Vector3.forward * distance;

            Vector3 extents = worldBounds.extents + Vector3.one * padding;
            Vector3 cameraRight = rotation * Vector3.right;
            Vector3 cameraUp = rotation * Vector3.up;
            float horizontalExtent = 0f;
            float verticalExtent = 0f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = new Vector3(x * extents.x, y * extents.y, z * extents.z);
                        horizontalExtent = Mathf.Max(
                            horizontalExtent,
                            Mathf.Abs(Vector3.Dot(corner, cameraRight)));
                        verticalExtent = Mathf.Max(
                            verticalExtent,
                            Mathf.Abs(Vector3.Dot(corner, cameraUp)));
                    }
                }
            }

            float aspect = viewWidth / viewHeight;
            worldCamera.orthographicSize = Mathf.Max(
                verticalExtent,
                horizontalExtent / Mathf.Max(0.1f, aspect)) * 1.04f;
            fittedWidth = Screen.width;
            fittedHeight = Screen.height;
            fittedSafeArea = safeArea;
        }
    }
}

