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

// Hermes runtime (phase 1).

public sealed record AiImage(string Mime, byte[] Data);
