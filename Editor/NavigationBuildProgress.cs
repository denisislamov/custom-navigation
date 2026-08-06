using System;
using System.Diagnostics;
using UnityEditor;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// The user pressed Cancel in the progress bar. This is not a build failure:
    /// the caller should show a neutral message instead of an exception.
    /// </summary>
    internal sealed class NavigationBuildCanceledException : Exception
    {
        public NavigationBuildCanceledException(string stage)
            : base("Operation canceled by the user at stage: " + stage)
        {
            Stage = stage;
        }

        public string Stage { get; }
    }

    /// <summary>
    /// Cancelable progress bar for long navigation editor operations.
    /// This is not a background process: it lives exactly as long as one operation
    /// started by the user with a button.
    /// </summary>
    internal sealed class NavigationBuildProgress : IDisposable
    {
        private readonly string title;
        private readonly int stageCount;
        private readonly bool cancelable;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        private int stageIndex;
        private string currentStage = string.Empty;
        private bool disposed;

        public NavigationBuildProgress(string title, int stageCount, bool cancelable = true)
        {
            this.title = title;
            this.stageCount = UnityEngine.Mathf.Max(1, stageCount);
            this.cancelable = cancelable;
        }

        public double ElapsedSeconds => stopwatch.Elapsed.TotalSeconds;

        public string CurrentStage => currentStage;

        /// <summary>Advances to the next stage and refreshes the progress bar.</summary>
        public void Stage(string description)
        {
            stageIndex++;
            currentStage = description;
            Display(description, (float)(stageIndex - 1) / stageCount);
        }

        /// <summary>Updates progress inside the current stage (for example, 12 / 48 sources).</summary>
        public void Report(string description, float stageProgress)
        {
            float baseProgress = (float)UnityEngine.Mathf.Max(0, stageIndex - 1) / stageCount;
            float span = 1f / stageCount;
            Display(description, baseProgress + span * UnityEngine.Mathf.Clamp01(stageProgress));
        }

        /// <summary>Throws <see cref="NavigationBuildCanceledException"/> when the user pressed Cancel.</summary>
        public void ThrowIfCanceled()
        {
            Display(currentStage, (float)stageIndex / stageCount);
        }

        private void Display(string description, float progress)
        {
            if (disposed)
            {
                return;
            }

            string info = $"[{stageIndex}/{stageCount}] {description}";
            if (!cancelable)
            {
                EditorUtility.DisplayProgressBar(title, info, UnityEngine.Mathf.Clamp01(progress));
                return;
            }

            if (EditorUtility.DisplayCancelableProgressBar(title, info, UnityEngine.Mathf.Clamp01(progress)))
            {
                throw new NavigationBuildCanceledException(description);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopwatch.Stop();
            EditorUtility.ClearProgressBar();
        }
    }
}

