using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MLPlatform.Storage;

public sealed class S3Exception(string message) : Exception(message);
