using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Revision diff (round 1 §6 DiffRevisions): changed lines grouped by pane, and whether the recipe itself changed in between.</summary>
public sealed record RevisionDiffView(int From, int To, bool ManifestChanged, IReadOnlyList<DraftDiff> Rows);
