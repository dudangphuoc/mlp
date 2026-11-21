using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MLPlatform.Storage;

/// <summary>Who will use the presigned URL: browser/SDK (PublicEndpoint) or containers on PCs (WorkloadEndpoint, via the overlay).</summary>
/// <summary>Who uses the signed URL: browser/agent (Public), workload containers (Workload), the control plane itself (Internal, the internal endpoint).</summary>
public enum Audience { Public, Workload, Internal }
