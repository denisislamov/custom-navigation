using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CustomNavigation.Authoring
{
    [CreateAssetMenu(
        fileName = "NavigationAgentProfile",
        menuName = "Custom Navigation/Agent Profile")]
    public sealed class NavigationAgentProfile : ScriptableObject
    {
        [SerializeField, Tooltip("Stable profile id. Must match on the client and on the server.")]
        private string profileId = "human_standing";
        [SerializeField, Min(0.01f), Tooltip("Full agent height in meters. Defines the minimum vertical clearance.")]
        private float height = 1.8f;
        [SerializeField, Min(0.01f), Tooltip("Agent radius in meters. The navmesh keeps at least this distance from walls.")]
        private float radius = 0.45f;
        [SerializeField, Min(0f), Tooltip("Maximum step height the agent can climb without a dedicated link.")]
        private float maximumClimb = 0.35f;
        [SerializeField, Range(0f, 89f), Tooltip("Maximum walkable surface slope in degrees.")]
        private float maximumSlope = 45f;
        [SerializeField, FormerlySerializedAs("includedPolygonFlags"), Tooltip("Which movement types are available to the agent.")]
        private NavigationFlags allowedMovement = NavigationFlags.Walk
                                                  | NavigationFlags.Crouch
                                                  | NavigationFlags.Swim
                                                  | NavigationFlags.Jump
                                                  | NavigationFlags.Door
                                                  | NavigationFlags.Ladder;
        [SerializeField, FormerlySerializedAs("excludedPolygonFlags"), Tooltip("Which movement types are always forbidden. Takes priority over Allowed Movement.")]
        private NavigationFlags forbiddenMovement = NavigationFlags.Disabled;
        [SerializeField, Tooltip("Per-agent overrides for surface type costs.")]
        private List<NavigationAreaCost> areaCosts = new List<NavigationAreaCost>();

        public string ProfileId => profileId;
        public float Height => height;
        public float Radius => radius;
        public float MaximumClimb => maximumClimb;
        public float MaximumSlope => maximumSlope;
        public NavigationFlags AllowedMovement => allowedMovement;
        public NavigationFlags ForbiddenMovement => forbiddenMovement;
        public int IncludedPolygonFlags => NavigationFlagsUtility.ToMask(allowedMovement);
        public int ExcludedPolygonFlags => NavigationFlagsUtility.ToMask(forbiddenMovement);
        public IReadOnlyList<NavigationAreaCost> AreaCosts => areaCosts;

        public float GetAreaCost(int areaId)
        {
            for (int i = 0; i < areaCosts.Count; i++)
            {
                if (areaCosts[i].AreaId == areaId)
                {
                    return areaCosts[i].Cost;
                }
            }

            return 1f;
        }

        public float GetAreaCost(NavigationArea area)
        {
            return GetAreaCost((int)area);
        }

        private void OnValidate()
        {
            profileId = NavigationIdUtility.Sanitize(profileId, "agent");
            height = Mathf.Max(0.01f, height);
            radius = Mathf.Max(0.01f, radius);
            maximumClimb = Mathf.Max(0f, maximumClimb);
            maximumSlope = Mathf.Clamp(maximumSlope, 0f, 89f);
            areaCosts ??= new List<NavigationAreaCost>();
            for (int i = 0; i < areaCosts.Count; i++)
            {
                areaCosts[i] ??= new NavigationAreaCost();
                areaCosts[i].Validate();
            }
        }
    }
}
