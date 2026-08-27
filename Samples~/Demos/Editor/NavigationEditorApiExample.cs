using CustomNavigation.Authoring;
using CustomNavigation.Editor.Api;

namespace CustomNavigation.Samples.Editor
{
    /// <summary>Minimal standalone and externally managed calls for consumer editor adapters.</summary>
    public static class NavigationEditorApiExample
    {
        public static NavigationEditorResult ValidateStandalone(NavigationLevel level)
        {
            return NavigationEditorApi.Validate(level, NavigationLevelIdBinding.Standalone);
        }

        public static NavigationEditorResult BakeManaged(
            NavigationLevel level,
            string owner,
            string levelId)
        {
            return NavigationEditorApi.Bake(
                level,
                NavigationLevelIdBinding.External(owner, levelId));
        }
    }
}
