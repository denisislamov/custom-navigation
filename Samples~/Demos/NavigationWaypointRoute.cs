using System.Collections.Generic;
using CustomNavigation.Authoring;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    public enum NavigationWaypointPatrolMode
    {
        Loop,
        PingPong,
        Once
    }

    /// <summary>
    /// List of waypoint positions patrolled by <see cref="NavigationBotAgent"/>.
    /// Add child Transforms (or references to any Transform in the scene)
    /// - the route is drawn in the Scene View while the navigation highlight is on.
    /// </summary>
    [AddComponentMenu("Custom Navigation/Waypoint Route")]
    [DisallowMultipleComponent]
    public sealed class NavigationWaypointRoute : MonoBehaviour
    {
        [SerializeField, Tooltip("Route positions. Drag in any Transform from the scene.")]
        private List<Transform> waypoints = new List<Transform>();
        [SerializeField, Tooltip("Loop returns to the first point after the last one; PingPong reverses; Once stops at the last point.")]
        private NavigationWaypointPatrolMode patrolMode = NavigationWaypointPatrolMode.Loop;
        [SerializeField, Tooltip("Gizmo sphere radius for each waypoint in the Scene View.")]
        private float gizmoRadius = 0.3f;

        public IReadOnlyList<Transform> Waypoints => waypoints;
        public NavigationWaypointPatrolMode PatrolMode => patrolMode;
        public int Count => waypoints.Count;

        /// <summary>
        /// Returns the world position of the waypoint at the given index.
        /// </summary>
        public bool TryGetPosition(int index, out Vector3 position)
        {
            if (index < 0 || index >= waypoints.Count || waypoints[index] == null)
            {
                position = default;
                return false;
            }

            position = waypoints[index].position;
            return true;
        }

        /// <summary>
        /// The next index after <paramref name="current"/> according to <see cref="patrolMode"/>.
        /// <paramref name="direction"/> becomes -1 when PingPong reverses.
        /// Returns -1 when the patrol is finished (Once reached the end).
        /// </summary>
        public int NextIndex(int current, ref int direction)
        {
            if (waypoints.Count == 0)
            {
                return -1;
            }

            if (waypoints.Count == 1)
            {
                return 0;
            }

            switch (patrolMode)
            {
                case NavigationWaypointPatrolMode.Once:
                    int next = current + direction;
                    return next >= 0 && next < waypoints.Count ? next : -1;

                case NavigationWaypointPatrolMode.PingPong:
                    int pingNext = current + direction;
                    if (pingNext < 0)
                    {
                        direction = 1;
                        pingNext = 1;
                    }
                    else if (pingNext >= waypoints.Count)
                    {
                        direction = -1;
                        pingNext = waypoints.Count - 2;
                    }

                    return pingNext;

                default:
                    return (current + 1) % waypoints.Count;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.Enabled || waypoints == null || waypoints.Count == 0)
            {
                return;
            }

            DrawRouteGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (NavigationHighlightSettings.Enabled || waypoints == null || waypoints.Count == 0)
            {
                return;
            }

            DrawRouteGizmo();
        }

        private void DrawRouteGizmo()
        {
            Color waypointColor = new Color(0.95f, 0.6f, 0.1f, 0.9f);
            Color lineColor = new Color(0.95f, 0.6f, 0.1f, 0.5f);
            Gizmos.color = waypointColor;
            Vector3? prev = null;
            int validCount = 0;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] == null)
                {
                    continue;
                }

                Vector3 pos = waypoints[i].position;
                Gizmos.DrawWireSphere(pos, gizmoRadius);
                if (prev.HasValue)
                {
                    Gizmos.color = lineColor;
                    DrawArrowLine(prev.Value, pos);
                    Gizmos.color = waypointColor;
                }

                prev = pos;
                validCount++;
            }

            if (validCount > 1 && patrolMode == NavigationWaypointPatrolMode.Loop)
            {
                Vector3 first = default;
                bool foundFirst = false;
                for (int i = 0; i < waypoints.Count; i++)
                {
                    if (waypoints[i] != null)
                    {
                        if (!foundFirst) { first = waypoints[i].position; foundFirst = true; }
                    }
                }

                if (foundFirst && prev.HasValue)
                {
                    Gizmos.color = lineColor;
                    DrawArrowLine(prev.Value, first);
                }
            }
        }

        private static void DrawArrowLine(Vector3 from, Vector3 to)
        {
            Gizmos.DrawLine(from, to);
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            Vector3 forward = dir / len;
            Vector3 side = Vector3.Cross(forward, Vector3.up);
            if (side.sqrMagnitude < 0.001f) side = Vector3.Cross(forward, Vector3.right);
            side = side.normalized * 0.18f;
            float headLen = Mathf.Min(0.3f, len * 0.2f);
            Vector3 tip = Vector3.Lerp(from, to, 0.65f);
            Gizmos.DrawLine(tip, tip - forward * headLen + side);
            Gizmos.DrawLine(tip, tip - forward * headLen - side);
        }
#endif
    }
}
