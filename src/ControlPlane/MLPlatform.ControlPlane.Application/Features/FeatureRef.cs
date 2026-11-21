using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

/// <summary>Feature reference `view:feature` or `view@2:feature` (no version = latest ready version).</summary>
public sealed record FeatureRef(string View, int Version, string Feature)
{
    public static FeatureRef Parse(string s)
    {
        var colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1) throw new InvalidArgumentException("FEATURE_REF_INVALID", $"Reference '{s}' must be in the form view:feature");
        var view = s[..colon];
        var at = view.IndexOf('@');
        var version = 0;
        if (at > 0 && !int.TryParse(view[(at + 1)..], out version)) throw new InvalidArgumentException("FEATURE_REF_INVALID", $"Invalid version in '{s}'");
        return new FeatureRef(at > 0 ? view[..at] : view, version, s[(colon + 1)..]);
    }
}
