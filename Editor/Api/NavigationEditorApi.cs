using System;
using System.Collections.Generic;
using System.IO;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor.Api
{
    /// <summary>Who supplies the level identity used by an editor operation.</summary>
    public enum NavigationLevelIdOwnership
    {
        /// <summary>The standalone Navigation Level owns its serialized id.</summary>
        Standalone = 0,

        /// <summary>An external editor tool supplies the id explicitly for this operation.</summary>
        ExternalManaged,
    }

    /// <summary>Explicit identity binding for validation, bake and summary reads.</summary>
    public sealed class NavigationLevelIdBinding
    {
        private static readonly NavigationLevelIdBinding StandaloneValue =
            new NavigationLevelIdBinding(NavigationLevelIdOwnership.Standalone, null, null);

        private NavigationLevelIdBinding(
            NavigationLevelIdOwnership ownership,
            string levelId,
            string owner)
        {
            Ownership = ownership;
            LevelId = levelId;
            Owner = owner;
        }

        /// <summary>Uses the id serialized by <see cref="NavigationLevel"/>.</summary>
        public static NavigationLevelIdBinding Standalone => StandaloneValue;

        /// <summary>Creates an external binding without referencing the owner assembly.</summary>
        public static NavigationLevelIdBinding External(string owner, string levelId)
        {
            return new NavigationLevelIdBinding(
                NavigationLevelIdOwnership.ExternalManaged,
                levelId,
                owner);
        }

        public NavigationLevelIdOwnership Ownership { get; }
        public string LevelId { get; }
        public string Owner { get; }
    }

    /// <summary>State of a consumer-facing editor operation or read-only summary.</summary>
    public enum NavigationEditorResultStatus
    {
        Missing = 0,
        Valid,
        Ready,
        Changed,
        Failed,
    }

    /// <summary>Read-only result shared by standalone tools and external editor adapters.</summary>
    public sealed class NavigationEditorResult
    {
        private readonly NavigationBakeIssue[] issues;

        internal NavigationEditorResult(
            NavigationEditorResultStatus status,
            NavigationLevelIdOwnership ownership,
            string owner,
            string levelId,
            NavigationArtifactAsset artifact,
            string artifactPath,
            string payloadPath,
            string manifestPath,
            string digest,
            int payloadSize,
            int polygonCount,
            int sourceMeshCount,
            IReadOnlyList<NavigationBakeIssue> sourceIssues)
        {
            Status = status;
            Ownership = ownership;
            Owner = owner ?? string.Empty;
            LevelId = levelId ?? string.Empty;
            Artifact = artifact;
            ArtifactPath = artifactPath ?? string.Empty;
            PayloadPath = payloadPath ?? string.Empty;
            ManifestPath = manifestPath ?? string.Empty;
            Digest = digest ?? string.Empty;
            PayloadSize = payloadSize;
            PolygonCount = polygonCount;
            SourceMeshCount = sourceMeshCount;
            issues = new NavigationBakeIssue[sourceIssues?.Count ?? 0];
            if (sourceIssues != null)
            {
                for (int i = 0; i < sourceIssues.Count; i++) issues[i] = sourceIssues[i];
            }
        }

        public NavigationEditorResultStatus Status { get; }
        public NavigationLevelIdOwnership Ownership { get; }
        public string Owner { get; }
        public string LevelId { get; }
        public NavigationArtifactAsset Artifact { get; }
        public string ArtifactPath { get; }
        public string PayloadPath { get; }
        public string ManifestPath { get; }
        /// <summary>Full lowercase SHA-256 of the exact payload, when available.</summary>
        public string Digest { get; }
        public int PayloadSize { get; }
        public int PolygonCount { get; }
        public int SourceMeshCount { get; }
        public IReadOnlyList<NavigationBakeIssue> Issues => issues;
        public bool Succeeded => Status == NavigationEditorResultStatus.Valid
                                 || Status == NavigationEditorResultStatus.Ready;
        public bool HasStatistics => PayloadSize >= 0 && PolygonCount >= 0 && SourceMeshCount >= 0;
    }

    /// <summary>
    /// Minimal editor-only integration API. It owns no runtime loop and references no consumer
    /// assembly; NPI or another editor adapter supplies only a string owner and canonical id.
    /// </summary>
    public static class NavigationEditorApi
    {
        /// <summary>Validates authoring and identity without writing files or assigning ids.</summary>
        public static NavigationEditorResult Validate(
            NavigationLevel level,
            NavigationLevelIdBinding binding = null)
        {
            binding ??= NavigationLevelIdBinding.Standalone;
            string levelId = ResolveLevelId(level, binding, out List<NavigationValidationIssue> issues);
            if (!HasErrors(issues) && level != null)
            {
                issues.AddRange(NavigationAuthoringValidator.Validate(level, levelId));
            }

            NavigationEditorResultStatus status = HasErrors(issues)
                ? NavigationEditorResultStatus.Failed
                : NavigationEditorResultStatus.Valid;
            return Empty(status, level, binding, levelId, issues);
        }

        /// <summary>Runs the separate navigation bake and returns its verified delivery summary.</summary>
        public static NavigationEditorResult Bake(
            NavigationLevel level,
            NavigationLevelIdBinding binding = null)
        {
            binding ??= NavigationLevelIdBinding.Standalone;
            string levelId = ResolveLevelId(level, binding, out List<NavigationValidationIssue> issues);
            if (!HasErrors(issues) && level != null)
            {
                issues.AddRange(NavigationAuthoringValidator.Validate(level, levelId));
            }

            if (HasErrors(issues))
            {
                return Empty(NavigationEditorResultStatus.Failed, level, binding, levelId, issues);
            }

            try
            {
                NavigationArtifactBuildResult built = NavigationArtifactBuilder.BuildForClient(
                    level,
                    null,
                    binding.Ownership == NavigationLevelIdOwnership.ExternalManaged ? levelId : null);
                return FromArtifact(
                    NavigationEditorResultStatus.Ready,
                    binding,
                    built.Asset,
                    built.ClientDataPath,
                    built.ClientManifestPath,
                    issues);
            }
            catch (Exception exception)
            {
                issues.Add(Error("Navigation bake failed: " + exception.Message, level));
                return Empty(NavigationEditorResultStatus.Failed, level, binding, levelId, issues);
            }
        }

        /// <summary>
        /// Reads and verifies the current bake. It never assigns ids, bakes, imports assets,
        /// changes preview settings or writes files.
        /// </summary>
        public static NavigationEditorResult ReadSummary(
            NavigationLevel level,
            NavigationLevelIdBinding binding = null)
        {
            binding ??= NavigationLevelIdBinding.Standalone;
            string levelId = ResolveLevelId(level, binding, out List<NavigationValidationIssue> issues);
            if (HasErrors(issues))
            {
                return Empty(NavigationEditorResultStatus.Failed, level, binding, levelId, issues);
            }

            NavigationArtifactAsset artifact = NavigationArtifactBuilder.LoadClientArtifact(levelId);
            if (artifact == null)
            {
                return Empty(NavigationEditorResultStatus.Missing, level, binding, levelId, issues);
            }

            try
            {
                NavigationArtifactLoader.Load(artifact);
                if (!NavigationArtifactBuilder.TryValidateManifest(artifact, out string manifestError))
                {
                    throw new InvalidDataException("Manifest: " + manifestError);
                }

                string payloadPath = artifact.NavigationData != null
                    ? AssetDatabase.GetAssetPath(artifact.NavigationData)
                    : string.Empty;
                NavigationEditorResultStatus status = IsChanged(level, artifact)
                    ? NavigationEditorResultStatus.Changed
                    : NavigationEditorResultStatus.Ready;
                if (status == NavigationEditorResultStatus.Changed)
                {
                    issues.Add(new NavigationValidationIssue(
                        NavigationValidationSeverity.Warning,
                        "The saved navigation bake is out of date for the current scene.",
                        level,
                        NavigationValidationCategory.Geometry));
                }

                return FromArtifact(
                    status,
                    binding,
                    artifact,
                    payloadPath,
                    NavigationArtifactBuilder.GetClientManifestPath(artifact),
                    issues);
            }
            catch (Exception exception)
            {
                issues.Add(Error("Navigation artifact is invalid: " + exception.Message, artifact));
                return Empty(NavigationEditorResultStatus.Failed, level, binding, levelId, issues, artifact);
            }
        }

        private static string ResolveLevelId(
            NavigationLevel level,
            NavigationLevelIdBinding binding,
            out List<NavigationValidationIssue> issues)
        {
            issues = new List<NavigationValidationIssue>();
            if (level == null)
            {
                issues.Add(Error("No NavigationLevel was supplied."));
                return string.Empty;
            }

            string levelId;
            if (binding.Ownership == NavigationLevelIdOwnership.Standalone)
            {
                levelId = level.LevelId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(binding.Owner))
                {
                    issues.Add(Error(
                        "External-managed Level ID requires a non-empty owner label.",
                        level));
                }
                levelId = binding.LevelId;
            }

            if (!IsCanonical(levelId))
            {
                issues.Add(Error($"Level ID '{levelId}' is not canonical.", level));
            }
            return levelId ?? string.Empty;
        }

        private static bool IsCanonical(string levelId)
        {
            return !string.IsNullOrWhiteSpace(levelId)
                   && string.Equals(
                       NavigationIdUtility.Sanitize(levelId, "level"),
                       levelId,
                       StringComparison.Ordinal);
        }

        private static NavigationEditorResult FromArtifact(
            NavigationEditorResultStatus status,
            NavigationLevelIdBinding binding,
            NavigationArtifactAsset artifact,
            string payloadPath,
            string manifestPath,
            List<NavigationValidationIssue> issues)
        {
            var validation = new NavigationBakeValidationResult(issues);
            return new NavigationEditorResult(
                status,
                binding.Ownership,
                binding.Owner,
                artifact.LevelId,
                artifact,
                AssetDatabase.GetAssetPath(artifact),
                payloadPath,
                manifestPath,
                artifact.ArtifactHash,
                artifact.NavigationData != null ? artifact.NavigationData.bytes.Length : -1,
                artifact.PolygonCount,
                artifact.SourceMeshCount,
                validation.Issues);
        }

        private static NavigationEditorResult Empty(
            NavigationEditorResultStatus status,
            NavigationLevel level,
            NavigationLevelIdBinding binding,
            string levelId,
            List<NavigationValidationIssue> issues,
            NavigationArtifactAsset artifact = null)
        {
            bool canonical = IsCanonical(levelId);
            var validation = new NavigationBakeValidationResult(issues);
            return new NavigationEditorResult(
                status,
                binding.Ownership,
                binding.Owner,
                levelId,
                artifact,
                artifact != null ? AssetDatabase.GetAssetPath(artifact)
                    : canonical ? NavigationArtifactBuilder.GetClientAssetPath(levelId) : null,
                artifact?.NavigationData != null ? AssetDatabase.GetAssetPath(artifact.NavigationData)
                    : canonical ? NavigationArtifactBuilder.GetClientDataPath(levelId) : null,
                artifact != null ? NavigationArtifactBuilder.GetClientManifestPath(artifact)
                    : canonical ? NavigationArtifactBuilder.GetClientManifestPath(levelId) : null,
                artifact?.ArtifactHash,
                artifact?.NavigationData != null ? artifact.NavigationData.bytes.Length : -1,
                artifact != null ? artifact.PolygonCount : -1,
                artifact != null ? artifact.SourceMeshCount : -1,
                validation.Issues);
        }

        private static bool IsChanged(NavigationLevel level, NavigationArtifactAsset artifact)
        {
            if (level == null) return false;
            var meshes = new HashSet<MeshFilter>();
            NavigationGeometrySource[] sources = level.GeometryRoot
                .GetComponentsInChildren<NavigationGeometrySource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                NavigationGeometrySource source = sources[i];
                if (source.Mode != NavigationGeometryMode.Include) continue;
                MeshFilter[] candidates = source.IncludeChildren
                    ? source.GetComponentsInChildren<MeshFilter>(source.IncludeInactiveChildren)
                    : source.TryGetComponent(out MeshFilter ownMesh)
                        ? new[] { ownMesh }
                        : Array.Empty<MeshFilter>();
                for (int meshIndex = 0; meshIndex < candidates.Length; meshIndex++)
                {
                    if (candidates[meshIndex]?.sharedMesh != null) meshes.Add(candidates[meshIndex]);
                }
            }

            return meshes.Count != artifact.SourceMeshCount || level.gameObject.scene.isDirty;
        }

        private static bool HasErrors(List<NavigationValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == NavigationValidationSeverity.Error) return true;
            }
            return false;
        }

        private static NavigationValidationIssue Error(string message, UnityEngine.Object context = null)
        {
            return new NavigationValidationIssue(
                NavigationValidationSeverity.Error,
                message,
                context,
                NavigationValidationCategory.Identifiers);
        }
    }
}
