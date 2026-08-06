using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using DotRecast.Detour;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// One-shot navmesh analysis: narrow spots and overly steep polygons.
    ///
    /// Runs ONLY on a button press. The result is a ready mesh that is then
    /// simply drawn. No recomputation in OnSceneGUI or on scene events.
    /// </summary>
    internal sealed class NavigationNavmeshAnalysis : IDisposable
    {
        private static readonly Color NarrowColor = new Color(1f, 0.25f, 0.15f, 0.55f);
        private static readonly Color TightColor = new Color(1f, 0.7f, 0.1f, 0.45f);
        private static readonly Color SteepColor = new Color(1f, 0.35f, 0.9f, 0.55f);
        private static readonly Color NearSteepColor = new Color(1f, 0.75f, 0.35f, 0.4f);

        public Mesh Overlay { get; private set; }
        public string Summary { get; private set; } = string.Empty;
        public MessageType SummaryType { get; private set; } = MessageType.Info;
        public DateTime EvaluatedAt { get; private set; }
        public int FlaggedCount { get; private set; }
        public bool HasResult => Overlay != null || !string.IsNullOrEmpty(Summary);

        public void Dispose()
        {
            if (Overlay != null)
            {
                Object.DestroyImmediate(Overlay);
                Overlay = null;
            }

            Summary = string.Empty;
            FlaggedCount = 0;
        }

        // -- Narrow spots -------------------------------------------------------
        public void AnalyzeClearance(
            NavigationArtifactInstance instance,
            float clearanceThreshold,
            NavigationBuildProgress progress)
        {
            Dispose();
            EvaluatedAt = DateTime.Now;

            progress?.Stage("Collecting navmesh boundaries");
            List<Edge> boundary = CollectBoundaryEdges(instance.NavMesh);
            if (boundary.Count == 0)
            {
                Summary = "The navmesh has no boundary edges - there is nothing to analyze.";
                SummaryType = MessageType.Warning;
                return;
            }

            progress?.Stage("Measuring passage widths");
            var grid = new EdgeGrid(boundary, Mathf.Max(0.5f, clearanceThreshold * 4f));

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            int flagged = 0;
            int total = 0;
            float worst = float.MaxValue;

            IterateSurfaceTriangles(instance.NavMesh, (a, b, c) =>
            {
                total++;
                Vector3 centroid = (a + b + c) / 3f;
                float distance = grid.DistanceTo(centroid);
                if (distance >= clearanceThreshold)
                {
                    return;
                }

                worst = Mathf.Min(worst, distance);
                flagged++;
                Color color = distance < clearanceThreshold * 0.5f ? NarrowColor : TightColor;
                AppendTriangle(vertices, colors, indices, a, b, c, color);
            });

            FlaggedCount = flagged;
            if (flagged == 0)
            {
                Summary =
                    $"No narrow spots: at least {clearanceThreshold:0.##} m of clearance everywhere. " +
                    $"Checked {total} triangles.";
                SummaryType = MessageType.Info;
                return;
            }

            Overlay = CreateMesh("Navigation Clearance Analysis", vertices, colors, indices);
            Summary =
                $"Found {flagged} narrow triangles out of {total}. " +
                $"Smallest clearance to the navmesh edge: {worst:0.##} m against a {clearanceThreshold:0.##} m threshold. " +
                "Red is critical, orange is tight.";
            SummaryType = MessageType.Warning;
        }

        // -- Steep surfaces -----------------------------------------------------
        public void AnalyzeSlopes(
            NavigationArtifactInstance instance,
            float maximumSlopeDegrees,
            NavigationBuildProgress progress)
        {
            Dispose();
            EvaluatedAt = DateTime.Now;
            progress?.Stage("Computing polygon slopes");

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            int flagged = 0;
            int total = 0;
            float steepest = 0f;
            float warningThreshold = maximumSlopeDegrees * 0.8f;

            IterateSurfaceTriangles(instance.NavMesh, (a, b, c) =>
            {
                total++;
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude <= Mathf.Epsilon)
                {
                    return;
                }

                normal.Normalize();
                float angle = Vector3.Angle(normal.y < 0f ? -normal : normal, Vector3.up);
                steepest = Mathf.Max(steepest, angle);
                if (angle < warningThreshold)
                {
                    return;
                }

                flagged++;
                Color color = angle >= maximumSlopeDegrees ? SteepColor : NearSteepColor;
                AppendTriangle(vertices, colors, indices, a, b, c, color);
            });

            FlaggedCount = flagged;
            if (flagged == 0)
            {
                Summary =
                    $"No steep surfaces: the maximum is {steepest:0.#} deg against the agent limit of " +
                    $"{maximumSlopeDegrees:0.#} deg. Checked {total} triangles.";
                SummaryType = MessageType.Info;
                return;
            }

            Overlay = CreateMesh("Navigation Slope Analysis", vertices, colors, indices);
            Summary =
                $"Found {flagged} sloped triangles out of {total}. " +
                $"Steepest: {steepest:0.#} deg against a {maximumSlopeDegrees:0.#} deg limit. " +
                "Pink is beyond the agent limit, orange is close to it.";
            SummaryType = steepest >= maximumSlopeDegrees ? MessageType.Warning : MessageType.Info;
        }

        // -- Navmesh surface traversal -------------------------------------------
        private static void IterateSurfaceTriangles(
            DtNavMesh navMesh,
            Action<Vector3, Vector3, Vector3> visit)
        {
            for (int tileIndex = 0; tileIndex < navMesh.GetMaxTiles(); tileIndex++)
            {
                DtMeshTile tile = navMesh.GetTile(tileIndex);
                DtMeshData data = tile?.data;
                if (data?.header == null || data.polys == null || data.verts == null)
                {
                    continue;
                }

                int polyCount = data.header.polyCount;
                for (int polyIndex = 0; polyIndex < polyCount && polyIndex < data.polys.Length; polyIndex++)
                {
                    DtPoly poly = data.polys[polyIndex];
                    if (poly == null || poly.GetPolyType() != 0 || poly.vertCount < 3)
                    {
                        continue;
                    }

                    for (int corner = 2; corner < poly.vertCount; corner++)
                    {
                        visit(
                            ReadVertex(data.verts, poly.verts[0]),
                            ReadVertex(data.verts, poly.verts[corner - 1]),
                            ReadVertex(data.verts, poly.verts[corner]));
                    }
                }
            }
        }

        private static List<Edge> CollectBoundaryEdges(DtNavMesh navMesh)
        {
            var edges = new List<Edge>();
            for (int tileIndex = 0; tileIndex < navMesh.GetMaxTiles(); tileIndex++)
            {
                DtMeshTile tile = navMesh.GetTile(tileIndex);
                DtMeshData data = tile?.data;
                if (data?.header == null || data.polys == null || data.verts == null)
                {
                    continue;
                }

                int polyCount = data.header.polyCount;
                for (int polyIndex = 0; polyIndex < polyCount && polyIndex < data.polys.Length; polyIndex++)
                {
                    DtPoly poly = data.polys[polyIndex];
                    if (poly == null || poly.GetPolyType() != 0 || poly.vertCount < 3)
                    {
                        continue;
                    }

                    for (int corner = 0; corner < poly.vertCount; corner++)
                    {
                        bool boundary = poly.neis == null
                                        || corner >= poly.neis.Length
                                        || poly.neis[corner] == 0;
                        if (!boundary)
                        {
                            continue;
                        }

                        int next = corner + 1 < poly.vertCount ? corner + 1 : 0;
                        edges.Add(new Edge(
                            ReadVertex(data.verts, poly.verts[corner]),
                            ReadVertex(data.verts, poly.verts[next])));
                    }
                }
            }

            return edges;
        }

        private static Vector3 ReadVertex(float[] source, int vertexIndex)
        {
            int offset = vertexIndex * 3;
            return new Vector3(source[offset], source[offset + 1], source[offset + 2]);
        }

        private static void AppendTriangle(
            List<Vector3> vertices,
            List<Color> colors,
            List<int> indices,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Color color)
        {
            Vector3 lift = Vector3.up * 0.05f;
            indices.Add(vertices.Count);
            vertices.Add(a + lift);
            colors.Add(color);
            indices.Add(vertices.Count);
            vertices.Add(b + lift);
            colors.Add(color);
            indices.Add(vertices.Count);
            vertices.Add(c + lift);
            colors.Add(color);
        }

        private static Mesh CreateMesh(
            string meshName,
            List<Vector3> vertices,
            List<Color> colors,
            List<int> indices)
        {
            if (indices.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private readonly struct Edge
        {
            public readonly Vector2 A;
            public readonly Vector2 B;

            public Edge(Vector3 a, Vector3 b)
            {
                A = new Vector2(a.x, a.z);
                B = new Vector2(b.x, b.z);
            }

            public float DistanceTo(Vector2 point)
            {
                Vector2 direction = B - A;
                float lengthSquared = direction.sqrMagnitude;
                if (lengthSquared <= Mathf.Epsilon)
                {
                    return Vector2.Distance(point, A);
                }

                float t = Mathf.Clamp01(Vector2.Dot(point - A, direction) / lengthSquared);
                return Vector2.Distance(point, A + direction * t);
            }
        }

        /// <summary>Uniform XZ grid so that not every edge is tested for every point.</summary>
        private sealed class EdgeGrid
        {
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            private readonly List<Edge> edges;
            private readonly float cellSize;

            public EdgeGrid(List<Edge> source, float cell)
            {
                edges = source;
                cellSize = Mathf.Max(0.25f, cell);
                for (int i = 0; i < edges.Count; i++)
                {
                    Vector2 a = edges[i].A;
                    Vector2 b = edges[i].B;
                    int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / cellSize);
                    int maxX = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / cellSize);
                    int minY = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / cellSize);
                    int maxY = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / cellSize);
                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            Add(x, y, i);
                        }
                    }
                }
            }

            public float DistanceTo(Vector3 worldPoint)
            {
                var point = new Vector2(worldPoint.x, worldPoint.z);
                int centerX = Mathf.FloorToInt(point.x / cellSize);
                int centerY = Mathf.FloorToInt(point.y / cellSize);
                float best = float.MaxValue;

                // The search radius grows until the found distance is provably smaller
                // than the boundary of the inspected area.
                for (int ring = 0; ring <= 4; ring++)
                {
                    for (int x = centerX - ring; x <= centerX + ring; x++)
                    {
                        for (int y = centerY - ring; y <= centerY + ring; y++)
                        {
                            if (ring > 0
                                && Mathf.Abs(x - centerX) != ring
                                && Mathf.Abs(y - centerY) != ring)
                            {
                                continue;
                            }

                            if (!cells.TryGetValue(Key(x, y), out List<int> bucket))
                            {
                                continue;
                            }

                            for (int i = 0; i < bucket.Count; i++)
                            {
                                best = Mathf.Min(best, edges[bucket[i]].DistanceTo(point));
                            }
                        }
                    }

                    if (best <= ring * cellSize)
                    {
                        return best;
                    }
                }

                return best;
            }

            private void Add(int x, int y, int edgeIndex)
            {
                long key = Key(x, y);
                if (!cells.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>();
                    cells[key] = bucket;
                }

                bucket.Add(edgeIndex);
            }

            private static long Key(int x, int y)
            {
                return ((long)x << 32) ^ (uint)y;
            }
        }
    }
}

