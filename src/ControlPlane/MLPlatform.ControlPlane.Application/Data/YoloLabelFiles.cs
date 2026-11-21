namespace MLPlatform.ControlPlane.Application.Data;

public static class YoloLabelFiles
{
    /// <summary>The YOLO label file that may sit next to an image: …/images/x.jpg ↔ …/labels/x.txt (or yololabels), or
    /// x.txt in the same folder.</summary>
    public static IReadOnlyList<string> CandidatesFor(string imagePath)
    {
        var stem = imagePath[..^Path.GetExtension(imagePath).Length];
        var list = new List<string>();
        foreach (var dir in new[] { "/labels/", "/yololabels/", "/yolo_labels/" })
        {
            var c = (stem.StartsWith("images/", StringComparison.Ordinal) ? "/" + stem : stem).Replace("/images/", dir).TrimStart('/') + ".txt";
            if (c != stem + ".txt") list.Add(c);
        }
        list.Add(stem + ".txt");
        return list;
    }
}
