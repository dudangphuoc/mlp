using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/*
 * AI label proposals (Hermes phase 3, M05 §4.2). A project lists the sources it may use (AI models from the AI page, or the
 * workspace's own deployments), maps model labels to schema classes, and opts in to external APIs. Proposals are stored per
 * item as "suggestion sets" — pending until a person applies or dismisses them; applied labels become annotations with
 * source = model and their confidence. Calling the models happens in the api (LabelProposer).
 */

public sealed record AssistSettingsRow(Guid ProjectId, string SourcesJson, string ClassMappingJson, bool AllowExternal, double MinConfidence, DateTimeOffset UpdatedAt);
