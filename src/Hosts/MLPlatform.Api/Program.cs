using MLPlatform.Api.Commands;
using MLPlatform.Api.Composition;
using MLPlatform.Api.Grpc;
using MLPlatform.Api.Http;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Startup;

await ProbeCommand.RunIfRequestedAsync(args);
if (DbApplyCommand.IsRequested(args))
{
    await DbApplyCommand.RunAsync(args);
    return;
}
if (SurfaceDescription.IsRequested(args)) SurfaceDescription.PrepareConfiguration();

var builder = WebApplication.CreateSlimBuilder(args);
builder.Configuration.AddEnvironmentVariables("MLP__");
builder.Services.AddControlPlaneApi(builder.Configuration);

var app = builder.Build();
app.UseControlPlanePipeline();
app.MapGrpcApi();
app.MapHttpApi(builder.Configuration);

if (SurfaceDescription.IsRequested(args))
{
    SurfaceDescription.Write(args, builder.Services, app);
    return;
}

await StartupTasks.RunAsync(app, builder.Configuration);
app.Run();
