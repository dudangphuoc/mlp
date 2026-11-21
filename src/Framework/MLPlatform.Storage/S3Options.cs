using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MLPlatform.Storage;

public sealed record S3Options(string Endpoint, string AccessKey, string SecretKey, string Region = "us-east-1", string? PublicEndpoint = null,
    string? WorkloadEndpoint = null);
