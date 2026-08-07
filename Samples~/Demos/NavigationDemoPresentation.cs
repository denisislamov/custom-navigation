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
}
