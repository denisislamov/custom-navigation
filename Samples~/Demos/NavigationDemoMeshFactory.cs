using System.Collections.Generic;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    internal static class NavigationDemoMeshFactory
    {
        public static Mesh CreateCylinder(float radius, float height, int segments = 16)
        {
            segments = Mathf.Max(3, segments);
            float halfHeight = height * 0.5f;
            var vertices = new List<Vector3>(segments * 2 + 2);
            var triangles = new List<int>(segments * 12);

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, -halfHeight, z));
                vertices.Add(new Vector3(x, halfHeight, z));
            }

            int bottomCenter = vertices.Count;
            vertices.Add(new Vector3(0f, -halfHeight, 0f));
            int topCenter = vertices.Count;
            vertices.Add(new Vector3(0f, halfHeight, 0f));

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int bottom = i * 2;
                int top = bottom + 1;
                int nextBottom = next * 2;
                int nextTop = nextBottom + 1;

                triangles.Add(bottom);
                triangles.Add(top);
                triangles.Add(nextTop);
                triangles.Add(bottom);
                triangles.Add(nextTop);
                triangles.Add(nextBottom);

                triangles.Add(bottomCenter);
                triangles.Add(nextBottom);
                triangles.Add(bottom);
                triangles.Add(topCenter);
                triangles.Add(top);
                triangles.Add(nextTop);
            }

            var mesh = new Mesh { name = "Navigation demo agent" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
