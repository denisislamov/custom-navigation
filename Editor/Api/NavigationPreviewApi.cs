using System;
using CustomNavigation.Authoring;

namespace CustomNavigation.Editor.Api
{
    /// <summary>Immutable snapshot of the package's one shared Scene View preview state.</summary>
    public sealed class NavigationPreviewState
    {
        public NavigationPreviewState(
            bool sources,
            bool baked,
            bool runtime,
            NavigationPreviewScope scope,
            NavigationPreviewDepth depth)
        {
            if (!Enum.IsDefined(typeof(NavigationPreviewScope), scope))
                throw new ArgumentOutOfRangeException(nameof(scope));
            if (!Enum.IsDefined(typeof(NavigationPreviewDepth), depth))
                throw new ArgumentOutOfRangeException(nameof(depth));
            Sources = sources;
            Baked = baked;
            Runtime = runtime;
            Scope = scope;
            Depth = depth;
        }

        public bool Sources { get; }
        public bool Baked { get; }
        public bool Runtime { get; }
        public NavigationPreviewScope Scope { get; }
        public NavigationPreviewDepth Depth { get; }

        public NavigationPreviewState WithSources(bool value) =>
            new NavigationPreviewState(value, Baked, Runtime, Scope, Depth);
        public NavigationPreviewState WithBaked(bool value) =>
            new NavigationPreviewState(Sources, value, Runtime, Scope, Depth);
        public NavigationPreviewState WithRuntime(bool value) =>
            new NavigationPreviewState(Sources, Baked, value, Scope, Depth);
        public NavigationPreviewState WithScope(NavigationPreviewScope value) =>
            new NavigationPreviewState(Sources, Baked, Runtime, value, Depth);
        public NavigationPreviewState WithDepth(NavigationPreviewDepth value) =>
            new NavigationPreviewState(Sources, Baked, Runtime, Scope, value);
    }

    /// <summary>
    /// Public access to the exact EditorPrefs state used by the package overlay. Reading is
    /// side-effect free; applying updates the existing preferences rather than a second toggle.
    /// </summary>
    public static class NavigationPreviewApi
    {
        public static NavigationPreviewState Current => new NavigationPreviewState(
            NavigationHighlightSettings.SourcesEnabled,
            NavigationHighlightSettings.BakedEnabled,
            NavigationHighlightSettings.RuntimeEnabled,
            NavigationHighlightSettings.Scope,
            NavigationHighlightSettings.Depth);

        public static event Action Changed
        {
            add => NavigationHighlightSettings.Changed += value;
            remove => NavigationHighlightSettings.Changed -= value;
        }

        public static void Apply(NavigationPreviewState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            NavigationHighlightSettings.Apply(
                state.Sources,
                state.Baked,
                state.Runtime,
                state.Scope,
                state.Depth);
        }
    }
}
