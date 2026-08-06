using CustomNavigation.Runtime;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Tools → Custom Navigation → Create Navigation Bot Prefab
    /// Creates the bot prefab and a sample NavigationWaypointRoute in Generated/BotAgent.
    /// After creation:
    /// 1. Drag NavigationBotAgent.prefab into the scene.
    /// 2. Assign NavigationQuerySchedulerBehaviour to the Navigation field.
    /// 3. Create an empty GameObject with NavigationWaypointRoute,
    ///    add child Transforms, and assign the route to the Route field.
    /// </summary>
    public static class NavigationBotPrefabBuilder
    {
        private const string OutputFolder = "Assets/CustomNavigation/Generated/BotAgent";
        private const string PrefabPath = OutputFolder + "/NavigationBotAgent.prefab";
        private const string RoutePrefabPath = OutputFolder + "/NavigationWaypointRoute.prefab";
        private const string MaterialPath = OutputFolder + "/BotAgent.mat";

        [MenuItem("Tools/Custom Navigation/Create Bot Agent Prefab", priority = 160)]
        public static void CreateBotAgentPrefab()
        {
            EnsureFolder(OutputFolder);

            Material botMat = LoadOrCreateMaterial(MaterialPath, new Color(0.3f, 0.7f, 1f));
            CreateBotPrefab(botMat);
            CreateRoutePrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[CustomNavigation] Bot prefabs created:\n" +
                $"  Bot:   {PrefabPath}\n" +
                $"  Route: {RoutePrefabPath}\n\n" +
                "Drag both into the scene. On the bot assign Navigation Scheduler and Route.",
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        }

        // ── Bot prefab ────────────────────────────────────────────────────────
        private static void CreateBotPrefab(Material mat)
        {
            // Capsule (radius=0.36, height=0.9 → visually like a character)
            var root = new GameObject("NavigationBotAgent");
            root.transform.localScale = new Vector3(0.72f, 0.72f, 0.72f);

            var mesh = CreateCapsuleMesh(0.5f, 1.25f, 12);
            var mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = SaveMesh(mesh, OutputFolder + "/BotAgent_Capsule.asset");
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            var agent = root.AddComponent<NavigationBotAgent>();
            _ = agent;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        // ── Route prefab ──────────────────────────────────────────────────────
        private static void CreateRoutePrefab()
        {
            var root = new GameObject("NavigationWaypointRoute");
            var route = root.AddComponent<NavigationWaypointRoute>();

            // Create 4 example waypoints as children
            string[] names = { "Waypoint_A", "Waypoint_B", "Waypoint_C", "Waypoint_D" };
            Vector3[] offsets =
            {
                new Vector3(-3f, 0f, -3f),
                new Vector3( 3f, 0f, -3f),
                new Vector3( 3f, 0f,  3f),
                new Vector3(-3f, 0f,  3f)
            };

            var wpTransforms = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var wp = new GameObject(names[i]);
                wp.transform.SetParent(root.transform, false);
                wp.transform.localPosition = offsets[i];
                wpTransforms[i] = wp.transform;
            }

            // Wire children into the waypoints list via SerializedObject
            var so = new SerializedObject(route);
            SerializedProperty wpList = so.FindProperty("waypoints");
            wpList.ClearArray();
            for (int i = 0; i < wpTransforms.Length; i++)
            {
                wpList.InsertArrayElementAtIndex(i);
                wpList.GetArrayElementAtIndex(i).objectReferenceValue = wpTransforms[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RoutePrefabPath);
            Object.DestroyImmediate(root);
        }

        // ── Mesh ──────────────────────────────────────────────────────────────
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                existing.vertices = mesh.vertices;
                existing.normals = mesh.normals;
                existing.triangles = mesh.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh CreateCapsuleMesh(float radius, float height, int segments)
        {
            // Build a simple capsule: cylinder body + hemisphere caps
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            var norms = new System.Collections.Generic.List<Vector3>();
            float half = height * 0.5f - radius;
            if (half < 0f) half = 0f;
            int rings = 6;

            // Bottom hemisphere
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * 0.5f * r / rings;
                float y = -half - Mathf.Sin(phi) * radius;
                float xzR = Mathf.Cos(phi) * radius;
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.PI * 2f * s / segments;
                    float x = Mathf.Cos(theta) * xzR;
                    float z = Mathf.Sin(theta) * xzR;
                    verts.Add(new Vector3(x, y, z));
                    norms.Add(new Vector3(x / radius, -(y + half) / radius, z / radius));
                }
            }

            // Top hemisphere
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * 0.5f * r / rings;
                float y = half + Mathf.Sin(phi) * radius;
                float xzR = Mathf.Cos(phi) * radius;
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.PI * 2f * s / segments;
                    float x = Mathf.Cos(theta) * xzR;
                    float z = Mathf.Sin(theta) * xzR;
                    verts.Add(new Vector3(x, y, z));
                    norms.Add(new Vector3(x / radius, (y - half) / radius, z / radius));
                }
            }

            // Triangles
            int cols = segments + 1;
            for (int section = 0; section < 2; section++)
            {
                int baseIndex = section * (rings + 1) * cols;
                for (int r = 0; r < rings; r++)
                {
                    int ringDir = section == 0 ? 1 : -1; // flip for bottom
                    for (int s = 0; s < segments; s++)
                    {
                        int a = baseIndex + r * cols + s;
                        int b = a + 1;
                        int c = a + cols;
                        int d = c + 1;
                        if (ringDir > 0)
                        {
                            tris.Add(a); tris.Add(c); tris.Add(b);
                            tris.Add(b); tris.Add(c); tris.Add(d);
                        }
                        else
                        {
                            tris.Add(a); tris.Add(b); tris.Add(c);
                            tris.Add(b); tris.Add(d); tris.Add(c);
                        }
                    }
                }
            }

            var m = new Mesh { name = "NavigationBot_Capsule" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static Material LoadOrCreateMaterial(string path, Color color)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            mat = new Material(shader)
            {
                color = color,
                name = System.IO.Path.GetFileNameWithoutExtension(path)
            };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
