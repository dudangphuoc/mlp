using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

public sealed class WorkspaceGrpc(IMediator mediator) : WorkspaceService.WorkspaceServiceBase
{
    public override async Task<Empty> UpdateWorkspace(UpdateWorkspaceRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RenameWorkspaceCommand(G(r.WorkspaceId, "workspace_id"), r.Name), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> UpdateMemberRole(UpdateMemberRoleRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new SetMemberRoleCommand(G(r.WorkspaceId, "workspace_id"), G(r.UserId, "user_id"), r.Role), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> RemoveMember(RemoveMemberRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RemoveMemberCommand(G(r.WorkspaceId, "workspace_id"), G(r.UserId, "user_id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ListInvitationsResponse> ListInvitations(ListInvitationsRequest r, ServerCallContext ctx)
    {
        var res = new ListInvitationsResponse();
        res.Invitations.AddRange((await mediator.Query(new ListInvitationsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(i => new InvitationInfo
        {
            Id = i.Id.ToString(), Email = i.Email, Role = i.Role, CreatedAt = Timestamp.FromDateTimeOffset(i.CreatedAt),
            ExpiresAt = Timestamp.FromDateTimeOffset(i.ExpiresAt), AcceptedAt = i.AcceptedAt is { } a ? Timestamp.FromDateTimeOffset(a) : null,
        }));
        return res;
    }

    public override async Task<Empty> RevokeInvitation(RevokeInvitationRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new RevokeInvitationCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ListWorkspacesResponse> ListWorkspaces(Empty r, ServerCallContext ctx)
    {
        var res = new ListWorkspacesResponse();
        res.Workspaces.AddRange((await mediator.Query(new ListWorkspacesQuery(), ctx.CancellationToken)).Select(Map.ToWorkspace));
        return res;
    }

    public override async Task<Workspace> CreateWorkspace(CreateWorkspaceRequest r, ServerCallContext ctx) =>
        (await mediator.Send(new CreateWorkspaceCommand(r.Slug, r.Name), ctx.CancellationToken)).ToWorkspace();

    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<ListMembersResponse> ListMembers(ListMembersRequest r, ServerCallContext ctx)
    {
        var res = new ListMembersResponse();
        res.Members.AddRange((await mediator.Query(new ListMembersQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(m => new Member
            { UserId = m.UserId.ToString(), Username = m.Username, DisplayName = m.DisplayName, Email = m.Email, Role = m.Role }));
        return res;
    }

    public override async Task<Invitation> CreateInvitation(CreateInvitationRequest r, ServerCallContext ctx)
    {
        var (token, exp) = await mediator.Send(new CreateInvitationCommand(G(r.WorkspaceId, "workspace_id"), r.Email, r.Role), ctx.CancellationToken);
        return new Invitation { Token = token, ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(exp) };
    }
}
