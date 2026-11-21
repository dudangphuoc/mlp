using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Catalog;

public sealed record TaskRow(string Key, string ModalityIn, string ModalityOut, string[] Converters, string PrimaryMetric, string[] Metrics);
