using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MLPlatform.Shim;

/// <summary>Decode Event{step=2, summary=5{value=1{tag=1, simple_value=2, tensor=8{float_val=5, double_val=6,
/// tensor_content=4}}}}.</summary>
internal static class TfEvent
{
    public static IEnumerable<MetricPoint> Parse(ReadOnlySpan<byte> ev)
    {
        long step = 0;
        var values = new List<(string, double)>();
        var r = new Wire(ev.ToArray());
        while (r.Next(out var field, out var type))
        {
            if (field == 2 && type == 0) step = (long)r.Varint();
            else if (field == 5 && type == 2) ParseSummary(r.Bytes(), values);
            else r.Skip(type);
        }
        return values.Select(v => new MetricPoint(v.Item1, step, v.Item2)).ToList();
    }

    private static void ParseSummary(byte[] s, List<(string, double)> values)
    {
        var r = new Wire(s);
        while (r.Next(out var f, out var t))
        {
            if (f != 1 || t != 2) { r.Skip(t); continue; }
            var v = new Wire(r.Bytes());
            string tag = "";
            double? val = null;
            while (v.Next(out var vf, out var vt))
            {
                if (vf == 1 && vt == 2) tag = Encoding.UTF8.GetString(v.Bytes());
                else if (vf == 2 && vt == 5) val = v.Float();
                else if (vf == 8 && vt == 2) val = ParseTensor(v.Bytes()) ?? val;
                else v.Skip(vt);
            }
            if (val is { } d && tag.Length > 0) values.Add((tag, d));
        }
    }

    private static double? ParseTensor(byte[] t)
    {
        var r = new Wire(t);
        int dtype = 0;
        byte[]? content = null;
        double? val = null;
        while (r.Next(out var f, out var ty))
        {
            if (f == 1 && ty == 0) dtype = (int)r.Varint();
            else if (f == 4 && ty == 2) content = r.Bytes();
            else if (f == 5 && ty == 2) { var b = r.Bytes(); if (b.Length >= 4) val = BinaryPrimitives.ReadSingleLittleEndian(b); }
            else if (f == 5 && ty == 5) val = r.Float();
            else if (f == 6 && ty == 2) { var b = r.Bytes(); if (b.Length >= 8) val = BinaryPrimitives.ReadDoubleLittleEndian(b); }
            else if (f == 6 && ty == 1) val = r.Double();
            else r.Skip(ty);
        }
        if (val is null && content is not null)
            val = dtype == 2 && content.Length >= 8 ? BinaryPrimitives.ReadDoubleLittleEndian(content)
                : content.Length >= 4 ? BinaryPrimitives.ReadSingleLittleEndian(content) : null;
        return val;
    }

    private sealed class Wire(byte[] b)
    {
        private int _p;
        public bool Next(out int field, out int type)
        {
            field = type = 0;
            if (_p >= b.Length) return false;
            var key = Varint();
            field = (int)(key >> 3);
            type = (int)(key & 7);
            return true;
        }
        public ulong Varint()
        {
            ulong res = 0;
            for (var shift = 0; shift < 64 && _p < b.Length; shift += 7)
            {
                var x = b[_p++];
                res |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
            }
            return res;
        }
        public byte[] Bytes() { var n = (int)Varint(); var r = b.AsSpan(_p, n).ToArray(); _p += n; return r; }
        public float Float() { var v = BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(_p)); _p += 4; return v; }
        public double Double() { var v = BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(_p)); _p += 8; return v; }
        public void Skip(int type)
        {
            switch (type)
            {
                case 0: Varint(); break;
                case 1: _p += 8; break;
                case 2: _p += (int)Varint(); break;
                case 5: _p += 4; break;
                default: _p = b.Length; break;
            }
        }
    }
}
