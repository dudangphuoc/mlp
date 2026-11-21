using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>A version with its dataset and its train/val/test split, as the training canvas shows it.</summary>
public sealed record VersionSplitInfo(VersionRow Version, string DatasetName, string Task, string State, IReadOnlyDictionary<string, long> Counts);
