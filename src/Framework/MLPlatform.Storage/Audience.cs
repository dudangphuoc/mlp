using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MLPlatform.Storage;

/// <summary>Who will use the presigned URL: browser/SDK (PublicEndpoint) or containers on PCs (WorkloadEndpoint, via
/// the overlay).</summary>
public enum Audience { Public, Workload, Internal }
