using MLPlatform.ControlPlane.Application.Serving;

namespace MLPlatform.Api.Http.Inference;

/// <summary>An endpoint key either resolves to its deployment or to the HTTP error to return.</summary>
public union EndpointResolution(DeploymentRow, IResult);
