using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CustomNavigation.Authoring
{
    [Serializable]
    public sealed class NavigationAreaDefinition
    {
        [SerializeField, FormerlySerializedAs("id"), Tooltip("Surface type. The numeric value equals the DotRecast area id.")]
        private NavigationArea area = NavigationArea.Ground;
        [SerializeField, Tooltip("Designer-friendly name of the surface type.")]
        private string name = "Ground";
        [SerializeField, Tooltip("Area color used in previews, gizmos and validation reports.")]
        private Color color = new Color(0.1f, 0.75f, 0.5f, 1f);
        [SerializeField, Min(1f), Tooltip("Base path cost multiplier for this surface.")]
        private float defaultCost = 1f;
        [SerializeField, FormerlySerializedAs("polygonFlags"), Tooltip("Which movement types are allowed on this surface.")]
        private NavigationFlags flags = NavigationFlags.Walk;

        public NavigationArea Area => area;
        public int Id => (int)area;
        public string Name => name;
        public Color Color => color;
        public float DefaultCost => defaultCost;
        public NavigationFlags Flags => flags;
        public int PolygonFlags => NavigationFlagsUtility.ToMask(flags);

        public NavigationAreaDefinition(
            NavigationArea areaType,
            string areaName,
            Color areaColor,
            float cost,
            NavigationFlags navigationFlags)
        {
            area = areaType;
            name = areaName;
            color = areaColor;
            defaultCost = cost;
            flags = navigationFlags;
        }

        internal void Validate()
        {
            name = string.IsNullOrWhiteSpace(name) ? area.ToString() : name.Trim();
            defaultCost = Mathf.Max(1f, defaultCost);
        }
    }

    [CreateAssetMenu(
        fileName = "NavigationAreaCatalog",
        menuName = "DataSakura/Custom Navigation/Area Catalog")]
    public sealed class NavigationAreaCatalog : ScriptableObject
    {
        [SerializeField, Tooltip("Project surface catalog. Defines highlight color, cost and allowed movement types.")]
        private List<NavigationAreaDefinition> areas = new List<NavigationAreaDefinition>();

        public IReadOnlyList<NavigationAreaDefinition> Areas => areas;

        public void ResetToDefaults()
        {
            areas = new List<NavigationAreaDefinition>
            {
                new NavigationAreaDefinition(
                    NavigationArea.Ground, "Ground", new Color(0.1f, 0.75f, 0.5f, 1f), 1f,
                    NavigationFlags.Walk),
                new NavigationAreaDefinition(
                    NavigationArea.Stairs, "Stairs", new Color(0.2f, 0.65f, 1f, 1f), 1.1f,
                    NavigationFlags.Walk),
                new NavigationAreaDefinition(
                    NavigationArea.Danger, "Danger", new Color(1f, 0.25f, 0.12f, 1f), 4f,
                    NavigationFlags.Walk),
                new NavigationAreaDefinition(
                    NavigationArea.Crouch, "Crouch", new Color(0.75f, 0.35f, 1f, 1f), 1.5f,
                    NavigationFlags.Crouch)
            };
        }

        /// <summary>
        /// Finds a surface definition by type. Returns null when it is not defined.
        /// </summary>
        public NavigationAreaDefinition Find(NavigationArea area)
        {
            for (int i = 0; i < areas.Count; i++)
            {
                if (areas[i] != null && areas[i].Area == area)
                {
                    return areas[i];
                }
            }

            return null;
        }

        private void OnValidate()
        {
            areas ??= new List<NavigationAreaDefinition>();
            for (int i = 0; i < areas.Count; i++)
            {
                areas[i] ??= new NavigationAreaDefinition(
                    NavigationArea.Ground, "Ground", Color.white, 1f, NavigationFlags.Walk);
                areas[i].Validate();
            }
        }
    }
}
