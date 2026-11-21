using System.Globalization;
using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>One page of a project's items with the people's names resolved.</summary>
public sealed record ItemPage(IReadOnlyList<ItemListRow> Items, long Total, IReadOnlyDictionary<Guid, string> Names, IReadOnlyDictionary<string, string>? Urls = null);
