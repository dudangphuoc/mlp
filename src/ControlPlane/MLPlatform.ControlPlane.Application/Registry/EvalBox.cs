using System.Globalization;
using System.Text.Json.Nodes;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>A box in pixels. Cls is the class name (normalized to lowercase for matching).</summary>
public readonly record struct EvalBox(string Cls, double X1, double Y1, double X2, double Y2, double Score = 1)
{
    public double Area => Math.Max(0, X2 - X1) * Math.Max(0, Y2 - Y1);

    public static double Iou(EvalBox a, EvalBox b)
    {
        var iw = Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1);
        var ih = Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1);
        if (iw <= 0 || ih <= 0) return 0;
        var inter = iw * ih;
        var union = a.Area + b.Area - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
