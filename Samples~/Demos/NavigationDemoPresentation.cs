using System;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    public sealed class NavigationDemoGuiScope : IDisposable
    {
        private readonly Matrix4x4 previousMatrix;

        internal NavigationDemoGuiScope(Rect safeArea, float scale, float edgeMargin)
        {
            previousMatrix = GUI.matrix;
            Scale = scale;
            Width = Mathf.Max(1f, safeArea.width / scale - edgeMargin * 2f);
            Height = Mathf.Max(1f, safeArea.height / scale - edgeMargin * 2f);
            IsNarrow = Width < 700f;

            float guiTop = Screen.height - safeArea.yMax;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(safeArea.x + edgeMargin * scale, guiTop + edgeMargin * scale, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f));
        }

        public float Scale { get; }
        public float Width { get; }
        public float Height { get; }
        public bool IsNarrow { get; }

        public void Dispose()
        {
            GUI.matrix = previousMatrix;
        }
    }

    public static class NavigationDemoPresentation
    {
        public const float EdgeMargin = 16f;
        public const float HeaderHeight = 176f;
        public const float FooterHeight = 58f;

        public static NavigationDemoGuiScope BeginSafeAreaGui()
        {
            Rect safeArea = GetSafeArea();
            return new NavigationDemoGuiScope(safeArea, CalculateGuiScale(safeArea), EdgeMargin);
        }

        public static float CalculateGuiScale(Rect safeArea)
        {
            bool portrait = safeArea.height >= safeArea.width;
            float scale = portrait
                ? safeArea.width / 540f
                : safeArea.height / 720f;
            return Mathf.Clamp(scale, 1f, 3f);
        }

        public static void DrawHeader(
            NavigationDemoGuiScope gui,
            string title,
            string body,
            string badge,
            GUIStyle titleStyle,
            GUIStyle bodyStyle,
            GUIStyle badgeStyle)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.025f, 0.04f, 0.06f, 0.94f);
            GUI.DrawTexture(new Rect(0f, 0f, gui.Width, HeaderHeight), Texture2D.whiteTexture);
            GUI.color = previousColor;

            titleStyle.wordWrap = true;
            bodyStyle.wordWrap = true;
            if (gui.IsNarrow)
            {
                GUI.Label(new Rect(12f, 8f, gui.Width - 24f, 32f), title, titleStyle);
                GUI.Label(new Rect(12f, 43f, Mathf.Min(310f, gui.Width - 24f), 28f), badge, badgeStyle);
                GUI.Label(new Rect(12f, 77f, gui.Width - 24f, 94f), body, bodyStyle);
                return;
            }

            float badgeWidth = Mathf.Min(310f, gui.Width * 0.38f);
            GUI.Label(new Rect(12f, 9f, gui.Width - badgeWidth - 36f, 34f), title, titleStyle);
            GUI.Label(new Rect(gui.Width - badgeWidth - 12f, 9f, badgeWidth, 30f), badge, badgeStyle);
            GUI.Label(new Rect(12f, 48f, gui.Width - 24f, 120f), body, bodyStyle);
        }

        public static Rect GetSafeArea()
        {
            Rect safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                return new Rect(0f, 0f, Screen.width, Screen.height);
            }

            return safeArea;
        }
    }

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
