using MLPlatform.Data;

namespace MLPlatform.Api.Http.Health;

/// <summary>Liveness, and readiness meaning PostgreSQL answers within 2 seconds.</summary>
public static class HealthEndpoints
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(2);

    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => "ok");
        app.MapGet("/health/ready", ReadyAsync);
    }

    private static async Task<IResult> ReadyAsync(DataSources db, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ReadyTimeout);
            await using var connection = await db.Write.OpenConnectionAsync(cts.Token);
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", connection);
            await cmd.ExecuteScalarAsync(cts.Token);
            return Results.Text("ready");
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            return Results.Text("db: " + e.Message, statusCode: 503);
        }
    }
}
