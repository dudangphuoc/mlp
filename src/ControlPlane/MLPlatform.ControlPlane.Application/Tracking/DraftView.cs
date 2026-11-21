using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Everything the canvas shows for a draft: the recipe, the draft, its changes, why it can or cannot train, and the chosen data.</summary>
public sealed record DraftView(ProjectRecipeView Recipe, DraftRow Draft, int BaseRevision, IReadOnlyList<DraftDiff> Changes,
    IReadOnlyList<string> Reasons, VersionSplitInfo? Dataset, bool CanTrial = false, TrialView? Trial = null, TrainEstimateView? Estimate = null,
    TrialRow? PassedTrial = null);
