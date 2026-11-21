using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Ai;
using MLPlatform.Cqrs;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Infrastructure;

public sealed record AiQuestion(string Id, string Type, string Instructions, IReadOnlyList<AiOption> Options);   // Type: choice | score | noul
