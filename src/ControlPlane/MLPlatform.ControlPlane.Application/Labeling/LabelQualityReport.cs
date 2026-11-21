using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record LabelQualityReport(ProjectRow Project, List<QualityRow> Annotators, List<CalibrationRow> Calibration, bool CanManage, Guid ViewerId);
