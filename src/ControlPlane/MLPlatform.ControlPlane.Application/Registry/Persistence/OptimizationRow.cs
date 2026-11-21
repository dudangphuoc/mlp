using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>One optimization run with its source version; <c>Report</c> is the job's report.json, <c>Promoted</c> candidate → version id.</summary>
public sealed record OptimizationRow(Guid Id, Guid WorkspaceId, Guid SourceVersionId, Guid ExportId, string Device, string Candidates, Guid? HostId,
    Guid? CalibDatasetVersionId, Guid CheckDatasetVersionId, double Conf, string State, string Error, Guid? JobId, string Report, string Promoted,
    DateTimeOffset CreatedAt, string ModelName, int SourceVersion, string Task);
