using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;

namespace CustomNavigation.Editor
{
    /// <summary>Stable public severity contract for editor bake validation.</summary>
    public enum NavigationBakeIssueSeverity { Info, Warning, Error }

    /// <summary>One authoring issue reported by <see cref="NavigationBakeCommand.Validate"/>.</summary>
    public readonly struct NavigationBakeIssue
    {
        public NavigationBakeIssueSeverity Severity { get; }
        public string Category { get; }
        public string Message { get; }
        public UnityEngine.Object Context { get; }
        public Action Fix { get; }
        public string FixLabel { get; }
        public bool CanFix => Fix != null;

        internal NavigationBakeIssue(NavigationValidationIssue issue)
        {
            Severity = issue.Severity == NavigationValidationSeverity.Error
                ? NavigationBakeIssueSeverity.Error
                : issue.Severity == NavigationValidationSeverity.Warning
                    ? NavigationBakeIssueSeverity.Warning
                    : NavigationBakeIssueSeverity.Info;
            Category = issue.Category.ToString();
            Message = issue.Message;
            Context = issue.Context;
            Fix = issue.Fix;
            FixLabel = issue.FixLabel;
        }
    }

    /// <summary>Typed result of a public navigation authoring validation run.</summary>
    public sealed class NavigationBakeValidationResult
    {
        private readonly NavigationBakeIssue[] issues;
        public IReadOnlyList<NavigationBakeIssue> Issues => issues;
        public bool Succeeded { get; }

        internal NavigationBakeValidationResult(List<NavigationValidationIssue> source)
        {
            issues = new NavigationBakeIssue[source.Count];
            bool succeeded = true;
            for (int i = 0; i < source.Count; i++)
            {
                issues[i] = new NavigationBakeIssue(source[i]);
                succeeded &= source[i].Severity != NavigationValidationSeverity.Error;
            }
            Succeeded = succeeded;
        }
    }

    /// <summary>Typed output of a successful client navigation bake.</summary>
    public sealed class NavigationBakeResult
    {
        public byte[] Data { get; }
        public string Hash { get; }
        public int PolygonCount { get; }
        public int SourceMeshCount { get; }
        public int ByteSize => Data?.Length ?? 0;
        public NavigationArtifactAsset Asset { get; }
        public string ClientDataPath { get; }
        public string ClientManifestPath { get; }
        public double ElapsedSeconds { get; }

        internal NavigationBakeResult(NavigationArtifactBuildResult result)
        {
            Data = result.Data;
            Hash = result.Hash;
            PolygonCount = result.PolygonCount;
            SourceMeshCount = result.SourceMeshCount;
            Asset = result.Asset;
            ClientDataPath = result.ClientDataPath;
            ClientManifestPath = result.ClientManifestPath;
            ElapsedSeconds = result.ElapsedSeconds;
        }
    }

    /// <summary>Stable public entry point for editor validation and client artifact baking.</summary>
    public static class NavigationBakeCommand
    {
        public static NavigationBakeValidationResult Validate(NavigationLevel level)
        {
            return new NavigationBakeValidationResult(NavigationAuthoringValidator.Validate(level));
        }

        public static NavigationBakeResult Execute(NavigationLevel level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            return new NavigationBakeResult(NavigationArtifactBuilder.BuildForClient(level));
        }
    }
}
