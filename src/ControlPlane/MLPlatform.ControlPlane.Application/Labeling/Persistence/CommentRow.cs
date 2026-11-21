using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record CommentRow(Guid Id, string Author, string Body, bool Resolved, DateTimeOffset CreatedAt);
