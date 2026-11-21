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

/*
 * Hermes runtime (phase 1). Every AI call goes through here and becomes one ai.runs row that the UI shows:
 * which models were candidates and why, which one answered, every attempt (status, latency, outcome), exact input/output,
 * tokens, cost, and — when the first choice was not used — the fallback and its reason. Nothing runs silently.
 *
 * Adapted from the learn project (Hermes): typed decision calls (System One wire format), provider connections with declared
 * models and roles. Fixed on purpose: hard deadline per call, one circuit breaker per model (kept across calls), per-connection
 * concurrency + RPM limits, health from real calls (not only tests), errors classified by HTTP status, explicit defaults.
 */

public sealed record AiImage(string Mime, byte[] Data);
