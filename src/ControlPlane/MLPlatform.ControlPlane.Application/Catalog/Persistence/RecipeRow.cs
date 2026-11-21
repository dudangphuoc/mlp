using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Catalog;

public sealed record RecipeRow(Guid Id, Guid? WorkspaceId, string Name, string DisplayName, string Family, string Description,
    string Maturity, string[] Tasks, string LicenseCode, string LicenseWeights, string LicenseNotes, bool RequiresAck, string Gpu,
    double MinVramGb, string Source, string Manifest, string State, string[] Selfcheck);
