using System.Runtime.CompilerServices;

// The project-side client editor assembly (bot inspector, prefab & demo scene builders)
// used to live in this same assembly. It relies on a handful of internal editor helpers
// (baking, scene tools, inspector GUI). Expose internals to it instead of widening the
// public API surface of the package.
[assembly: InternalsVisibleTo("CustomNavigation.Client.Editor")]

[assembly: InternalsVisibleTo("CustomNavigation.NavigationEditor.Tests")]
