using Google.Protobuf.Reflection;
using MLPlatform.Contracts.V1;

namespace MLPlatform.DocGen;

/// <summary>Proto files of the public API; agent and model server protocols are internal and not listed.</summary>
public static class ProtoCatalog
{
    public static readonly IReadOnlyList<FileDescriptor> Files =
    [
        AiReflection.Descriptor, AuditReflection.Descriptor, CatalogReflection.Descriptor, CommonReflection.Descriptor, DataReflection.Descriptor,
        FeatureReflection.Descriptor, FleetReflection.Descriptor, HubReflection.Descriptor, IamReflection.Descriptor, LabelingReflection.Descriptor,
        LlmopsReflection.Descriptor, MonitoringReflection.Descriptor, NotebookReflection.Descriptor, NotifyReflection.Descriptor, OpsReflection.Descriptor,
        PipelineReflection.Descriptor, RegistryReflection.Descriptor, RuntimeReflection.Descriptor, SchedulerReflection.Descriptor,
        ServingReflection.Descriptor, SettingsReflection.Descriptor, TrackingReflection.Descriptor, TuningReflection.Descriptor,
    ];
}
