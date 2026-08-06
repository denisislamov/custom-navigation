using UnityEngine;

namespace CustomNavigation.Authoring
{
    [CreateAssetMenu(
        fileName = "NavigationArtifact",
        menuName = "Custom Navigation/Navigation Artifact")]
    public sealed class NavigationArtifactAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Level id written by the editor exporter. Do not edit by hand.")]
        private string levelId;
        [SerializeField, Tooltip("SHA-256 of the navmesh binary. The client and the server compare this value.")]
        private string artifactHash;
        [SerializeField, Tooltip("Navigation artifact format version.")]
        private string schemaVersion;
        [SerializeField, Tooltip("DotRecast version that produced the artifact.")]
        private string dotRecastVersion;
        [SerializeField, Tooltip("Id of the agent profile used for the bake.")]
        private string agentProfileId;
        [SerializeField, Tooltip("Number of Detour polygons after the bake.")]
        private int polygonCount;
        [SerializeField, Tooltip("Number of source MeshFilters included in the bake.")]
        private int sourceMeshCount;
        [SerializeField, Tooltip("Serialized DtNavMesh binary, loaded without any runtime bake.")]
        private TextAsset navigationData;
        [SerializeField, TextArea(3, 12), Tooltip("Manifest exported together with the navmesh binary.")]
        private string manifestJson;

        public string LevelId => levelId;
        public string ArtifactHash => artifactHash;
        public string SchemaVersion => schemaVersion;
        public string DotRecastVersion => dotRecastVersion;
        public string AgentProfileId => agentProfileId;
        public int PolygonCount => polygonCount;
        public int SourceMeshCount => sourceMeshCount;
        public TextAsset NavigationData => navigationData;
        public string ManifestJson => manifestJson;

        public void Configure(
            string newLevelId,
            string newArtifactHash,
            string newSchemaVersion,
            string newDotRecastVersion,
            string newAgentProfileId,
            int newPolygonCount,
            int newSourceMeshCount,
            TextAsset newNavigationData,
            string newManifestJson)
        {
            levelId = newLevelId;
            artifactHash = newArtifactHash;
            schemaVersion = newSchemaVersion;
            dotRecastVersion = newDotRecastVersion;
            agentProfileId = newAgentProfileId;
            polygonCount = newPolygonCount;
            sourceMeshCount = newSourceMeshCount;
            navigationData = newNavigationData;
            manifestJson = newManifestJson;
        }
    }
}
