using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using DotRecast.Detour;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Draws the baked navmesh polygons in the Scene View while Navigation Highlight is enabled
    /// in the Custom Navigation window or Scene Preview preferences. Artifacts come from
    /// the scene NavigationQuerySchedulerBehaviour and from NavigationLevel by its level id.
    /// </summary>
    [InitializeOnLoad]
    internal static class NavigationHighlightOverlay
    {
        private const int GroundPolyType = 0;
        private const int DetailTriangleStride = 4;
        private const float SurfaceHeightOffset = 0.02f;
        private const float EdgeHeightOffset = 0.035f;

        private sealed class CachedOverlay
        {
            public string SourceKey;
            public Mesh Surface;
            public Mesh Edges;
            public string Error;

            public void Release()
            {
                if (Surface != null)
                {
                    Object.DestroyImmediate(Surface);
                    Surface = null;
                }

                if (Edges != null)
                {
                    Object.DestroyImmediate(Edges);
                    Edges = null;
                }
            }
        }

        private static readonly Dictionary<NavigationArtifactAsset, CachedOverlay> Overlays =
            new Dictionary<NavigationArtifactAsset, CachedOverlay>();

        private static readonly List<NavigationArtifactAsset> ActiveArtifacts =
            new List<NavigationArtifactAsset>();

        private static readonly List<NavigationLevel> ActiveLevels = new List<NavigationLevel>();

        private static readonly Dictionary<int, Color> AreaColors = new Dictionary<int, Color>();

        private static readonly List<Vector3> SurfaceVertices = new List<Vector3>();
        private static readonly List<Color> SurfaceColors = new List<Color>();
        private static readonly List<int> SurfaceIndices = new List<int>();
        private static readonly List<Vector3> EdgeVertices = new List<Vector3>();
        private static readonly List<Color> EdgeColors = new List<Color>();
        private static readonly List<int> EdgeIndices = new List<int>();

        private static Material overlayMaterial;
        private static bool sourcesDirty = true;
        private static int areaSignature;

        static NavigationHighlightOverlay()
        {
            SceneView.duringSceneGui += OnSceneGui;
            NavigationHighlightSettings.Changed += OnHighlightToggled;
            EditorApplication.hierarchyChanged += MarkSourcesDirty;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.quitting += ReleaseAll;
        }

        internal static void InvalidateArtifacts()
        {
            foreach (CachedOverlay overlay in Overlays.Values)
            {
                overlay.Release();
            }

            Overlays.Clear();
            sourcesDirty = true;
            SceneView.RepaintAll();
        }

        internal static IReadOnlyList<NavigationArtifactAsset> RefreshAndGetArtifacts()
        {
            CollectSources();
            RefreshAreaColors();
            sourcesDirty = false;
            return ActiveArtifacts;
        }

        internal static bool TryDescribeOverlay(
            NavigationArtifactAsset artifact,
            out int surfaceTriangles,
            out int edgeSegments,
            out string error)
        {
            CachedOverlay overlay = GetOrBuildOverlay(artifact);
            surfaceTriangles = overlay?.Surface != null ? overlay.Surface.vertexCount / 3 : 0;
            edgeSegments = overlay?.Edges != null ? overlay.Edges.vertexCount / 2 : 0;
            error = overlay?.Error;
            return error == null && surfaceTriangles > 0;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            MarkSourcesDirty();
            SceneView.RepaintAll();
        }

        private static void OnSceneClosed(Scene scene)
        {
            MarkSourcesDirty();
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            MarkSourcesDirty();
            SceneView.RepaintAll();
        }

        private static void OnHighlightToggled()
        {
            sourcesDirty = true;
            SceneView.RepaintAll();
        }

        private static void MarkSourcesDirty()
        {
            sourcesDirty = true;
        }

        private static void ReleaseAll()
        {
            foreach (CachedOverlay overlay in Overlays.Values)
            {
                overlay.Release();
            }

            Overlays.Clear();
            if (overlayMaterial != null)
            {
                Object.DestroyImmediate(overlayMaterial);
                overlayMaterial = null;
            }
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!NavigationHighlightSettings.Enabled
                || Event.current.type != EventType.Repaint
                || sceneView == null
                || sceneView.camera == null)
            {
                return;
            }

            if (sourcesDirty)
            {
                sourcesDirty = false;
                CollectSources();
            }

            RefreshAreaColors();
            if (ActiveArtifacts.Count == 0)
            {
                return;
            }

            EnsureMaterial();
            for (int i = 0; i < ActiveArtifacts.Count; i++)
            {
                NavigationArtifactAsset artifact = ActiveArtifacts[i];
                CachedOverlay overlay = GetOrBuildOverlay(artifact);
                if (overlay?.Surface == null)
                {
                    continue;
                }

                GL.PushMatrix();
                GL.MultMatrix(Handles.matrix);
                overlayMaterial.SetPass(0);
                Graphics.DrawMeshNow(overlay.Surface, Matrix4x4.identity);
                if (overlay.Edges != null)
                {
                    overlayMaterial.SetPass(0);
                    Graphics.DrawMeshNow(overlay.Edges, Matrix4x4.identity);
                }

                GL.PopMatrix();
            }
        }

        private static void EnsureMaterial()
        {
            if (overlayMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            overlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            overlayMaterial.SetInt("_Cull", (int)CullMode.Off);
            overlayMaterial.SetInt("_ZWrite", 0);
            overlayMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        }

        private static void CollectSources()
        {
            ActiveArtifacts.Clear();
            ActiveLevels.Clear();

            NavigationQuerySchedulerBehaviour[] schedulers =
                Object.FindObjectsByType<NavigationQuerySchedulerBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int i = 0; i < schedulers.Length; i++)
            {
                AddArtifact(schedulers[i].Artifact);
            }

            NavigationLevel[] levels = Object.FindObjectsByType<NavigationLevel>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < levels.Length; i++)
            {
                ActiveLevels.Add(levels[i]);
                string levelId = levels[i].LevelId;
                if (string.IsNullOrEmpty(levelId))
                {
                    continue;
                }

                AddArtifact(AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(
                    $"{NavigationArtifactBuilder.GeneratedClientFolder}/{levelId}.artifact.asset"));
            }
        }

        private static void AddArtifact(NavigationArtifactAsset artifact)
        {
            if (artifact != null && !ActiveArtifacts.Contains(artifact))
            {
                ActiveArtifacts.Add(artifact);
            }
        }

        private static CachedOverlay GetOrBuildOverlay(NavigationArtifactAsset artifact)
        {
            string sourceKey = $"{artifact.ArtifactHash}|{artifact.PolygonCount}|{areaSignature}";
            if (Overlays.TryGetValue(artifact, out CachedOverlay cached))
            {
                if (string.Equals(cached.SourceKey, sourceKey, StringComparison.Ordinal)
                    && (cached.Surface != null || cached.Error != null))
                {
                    return cached;
                }

                cached.Release();
                Overlays.Remove(artifact);
            }

            var overlay = new CachedOverlay { SourceKey = sourceKey };
            Overlays[artifact] = overlay;

            try
            {
                NavigationArtifactInstance instance = NavigationArtifactLoader.Load(artifact);
                BuildMeshes(instance.NavMesh, overlay);
            }
            catch (Exception exception)
            {
                overlay.Error = exception.Message;
                Debug.LogWarning(
                    $"[CustomNavigation] Navigation highlight cannot draw '{artifact.name}': " +
                    exception.Message,
                    artifact);
            }

            return overlay;
        }

        private static void BuildMeshes(DtNavMesh navMesh, CachedOverlay overlay)
        {
            SurfaceVertices.Clear();
            SurfaceColors.Clear();
            SurfaceIndices.Clear();
            EdgeVertices.Clear();
            EdgeColors.Clear();
            EdgeIndices.Clear();

            for (int tileIndex = 0; tileIndex < navMesh.GetMaxTiles(); tileIndex++)
            {
                DtMeshTile tile = navMesh.GetTile(tileIndex);
                DtMeshData data = tile?.data;
                if (data?.header == null || data.polys == null || data.verts == null)
                {
                    continue;
                }

                AppendTile(data);
            }

            if (SurfaceIndices.Count > 0)
            {
                overlay.Surface = CreateMesh(
                    "Navigation Highlight Surface",
                    SurfaceVertices,
                    SurfaceColors,
                    SurfaceIndices,
                    MeshTopology.Triangles);
            }

            if (EdgeIndices.Count > 0)
            {
                overlay.Edges = CreateMesh(
                    "Navigation Highlight Edges",
                    EdgeVertices,
                    EdgeColors,
                    EdgeIndices,
                    MeshTopology.Lines);
            }
        }

        private static void AppendTile(DtMeshData data)
        {
            int polyCount = data.header.polyCount;
            for (int polyIndex = 0; polyIndex < polyCount && polyIndex < data.polys.Length; polyIndex++)
            {
                DtPoly poly = data.polys[polyIndex];
                if (poly == null || poly.GetPolyType() != GroundPolyType || poly.vertCount < 3)
                {
                    continue;
                }

                Color fill = GetAreaColor(poly.GetArea());
                fill.a = NavigationHighlightPalette.NavigationMeshFillAlpha;
                AppendPolygonSurface(data, poly, polyIndex, fill);
                AppendPolygonEdges(data, poly);
            }
        }

        private static void AppendPolygonSurface(
            DtMeshData data,
            DtPoly poly,
            int polyIndex,
            Color fill)
        {
            bool hasDetail = data.detailMeshes != null
                             && polyIndex < data.detailMeshes.Length
                             && data.detailTris != null
                             && data.detailVerts != null;
            if (hasDetail)
            {
                DtPolyDetail detail = data.detailMeshes[polyIndex];
                if (detail.triCount > 0)
                {
                    for (int triangle = 0; triangle < detail.triCount; triangle++)
                    {
                        int triangleBase = (detail.triBase + triangle) * DetailTriangleStride;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int localIndex = data.detailTris[triangleBase + corner];
                            Vector3 position = localIndex < poly.vertCount
                                ? ReadVertex(data.verts, poly.verts[localIndex])
                                : ReadVertex(
                                    data.detailVerts,
                                    detail.vertBase + (localIndex - poly.vertCount));
                            AppendSurfaceVertex(position, fill);
                        }
                    }

                    return;
                }
            }

            for (int corner = 2; corner < poly.vertCount; corner++)
            {
                AppendSurfaceVertex(ReadVertex(data.verts, poly.verts[0]), fill);
                AppendSurfaceVertex(ReadVertex(data.verts, poly.verts[corner - 1]), fill);
                AppendSurfaceVertex(ReadVertex(data.verts, poly.verts[corner]), fill);
            }
        }

        private static void AppendPolygonEdges(DtMeshData data, DtPoly poly)
        {
            for (int corner = 0; corner < poly.vertCount; corner++)
            {
                int next = corner + 1 < poly.vertCount ? corner + 1 : 0;
                bool boundary = poly.neis == null
                                || corner >= poly.neis.Length
                                || poly.neis[corner] == 0;
                Color color = boundary
                    ? NavigationHighlightPalette.NavigationMeshBoundary
                    : NavigationHighlightPalette.NavigationMeshEdge;
                AppendEdgeVertex(ReadVertex(data.verts, poly.verts[corner]), color);
                AppendEdgeVertex(ReadVertex(data.verts, poly.verts[next]), color);
            }
        }

        private static void AppendSurfaceVertex(Vector3 position, Color color)
        {
            SurfaceIndices.Add(SurfaceVertices.Count);
            SurfaceVertices.Add(position + Vector3.up * SurfaceHeightOffset);
            SurfaceColors.Add(color);
        }

        private static void AppendEdgeVertex(Vector3 position, Color color)
        {
            EdgeIndices.Add(EdgeVertices.Count);
            EdgeVertices.Add(position + Vector3.up * EdgeHeightOffset);
            EdgeColors.Add(color);
        }

        private static Vector3 ReadVertex(float[] source, int vertexIndex)
        {
            int offset = vertexIndex * 3;
            return new Vector3(source[offset], source[offset + 1], source[offset + 2]);
        }

        private static Mesh CreateMesh(
            string meshName,
            List<Vector3> vertices,
            List<Color> colors,
            List<int> indices,
            MeshTopology topology)
        {
            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, topology, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void RefreshAreaColors()
        {
            AreaColors.Clear();
            for (int i = 0; i < ActiveLevels.Count; i++)
            {
                NavigationLevel level = ActiveLevels[i];
                NavigationAreaCatalog catalog = level != null ? level.AreaCatalog : null;
                if (catalog?.Areas == null)
                {
                    continue;
                }

                for (int areaIndex = 0; areaIndex < catalog.Areas.Count; areaIndex++)
                {
                    NavigationAreaDefinition area = catalog.Areas[areaIndex];
                    if (area != null)
                    {
                        AreaColors[area.Id] = area.Color;
                    }
                }
            }

            areaSignature = ComputeAreaSignature();
        }

        private static int ComputeAreaSignature()
        {
            int signature = 17;
            foreach (KeyValuePair<int, Color> entry in AreaColors)
            {
                unchecked
                {
                    signature += entry.Key * 31 + entry.Value.GetHashCode();
                }
            }

            return signature;
        }

        private static Color GetAreaColor(int areaId)
        {
            return AreaColors.TryGetValue(areaId, out Color color)
                ? color
                : NavigationHighlightPalette.NavigationMeshFallback;
        }
    }

    internal sealed class NavigationHighlightArtifactWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsArtifact(importedAssets)
                || ContainsArtifact(deletedAssets)
                || ContainsArtifact(movedAssets))
            {
                NavigationHighlightOverlay.InvalidateArtifacts();
            }
        }

        private static bool ContainsArtifact(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(".artifact.asset", StringComparison.OrdinalIgnoreCase)
                    || paths[i].EndsWith(".navmesh.bytes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
