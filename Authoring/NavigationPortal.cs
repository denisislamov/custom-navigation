using System.Collections.Generic;
using UnityEngine;

namespace CustomNavigation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Custom Navigation/Navigation Portal")]
    public sealed class NavigationPortal : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable portal id used by the runtime state, networking and telemetry.")]
        private string portalId;
        [SerializeField, Tooltip("Gameplay portal type: door, gate, bridge, lift or a scripted object.")]
        private NavigationPortalType portalType = NavigationPortalType.Door;
        [SerializeField, Tooltip("Initial portal state when the level starts.")]
        private bool openByDefault = true;
        [SerializeField, Tooltip("Navigation links whose availability is controlled by this portal.")]
        private List<NavigationLink> controlledLinks = new List<NavigationLink>();

        public string PortalId => portalId;
        public NavigationPortalType PortalType => portalType;
        public bool OpenByDefault => openByDefault;
        public IReadOnlyList<NavigationLink> ControlledLinks => controlledLinks;

        private void Reset()
        {
            portalId = NavigationIdUtility.CreateStableId("portal");
        }

        private void OnValidate()
        {
            portalId = string.IsNullOrWhiteSpace(portalId)
                ? NavigationIdUtility.CreateStableId("portal")
                : NavigationIdUtility.Sanitize(portalId, "portal");
            controlledLinks ??= new List<NavigationLink>();
        }

        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.SourcesEnabled)
            {
                return;
            }

            DrawPortalGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (NavigationHighlightSettings.SourcesEnabled)
            {
                return;
            }

            DrawPortalGizmo();
        }

        private void DrawPortalGizmo()
        {
            Vector3 position = transform.position;
            Gizmos.color = openByDefault
                ? NavigationHighlightPalette.PortalOpen
                : NavigationHighlightPalette.PortalClosed;
            Gizmos.DrawWireSphere(position, 0.4f);
            Gizmos.DrawLine(position, position + Vector3.up * 1.2f);

            if (controlledLinks == null)
            {
                return;
            }

            for (int i = 0; i < controlledLinks.Count; i++)
            {
                NavigationLink link = controlledLinks[i];
                if (link == null)
                {
                    continue;
                }

                Vector3 linkCenter = (link.WorldStart + link.WorldEnd) * 0.5f;
                Gizmos.DrawLine(position, linkCenter);
            }
        }
    }
}
