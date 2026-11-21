using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MLPlatform.Shim;

public readonly record struct MetricPoint(string Key, long Step, double Value);
