using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Everything one check needs, loaded with the caller's rights.</summary>
public sealed record HermesContext(Guid ActorId, ProjectRow Project, ItemStateRow State, string Classes, List<AnnotationRow> Annotations, HermesSource Source,
    string DatasetTask);
