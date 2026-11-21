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

/// <summary>Values a person can set on a draft.</summary>
public sealed record DraftInput(Guid ProjectRecipeId, string Variant, string ParamsJson, Guid? DatasetVersionId, string StartKind, string TrainKind,
    Guid? RetrainOfRunId, Guid? HostId, int Gpus, double Cpus, string RunName);
