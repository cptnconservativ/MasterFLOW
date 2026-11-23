using System.Globalization;
using System.Text;

namespace MasterFlow.Application.Field;

/// <summary>What a phone photo says about where and when it was taken.</summary>
public sealed record PhotoTag(string File, double? Latitude, double? Longitude, double? Altitude, DateTime? Taken, double? HorizontalError);

/// <summary>
/// Reads the GPS position and time from the EXIF block of a JPEG (phone photos of as-built structures, D-250). Only the
/// APP1 Exif segment is read: TIFF header, IFD0, the GPS IFD (latitude, longitude, altitude, horizontal error) and the
/// Exif IFD (DateTimeOriginal). Photos without GPS return nulls; files that are not JPEG return null.
/// </summary>
public static class ExifGps
{
    public static PhotoTag? Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileName(path));

    public static PhotoTag? Read(byte[] b, string name)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;
        int i = 2;
        while (i + 4 <= b.Length && b[i] == 0xFF)
        {
            byte marker = b[i + 1];
            if (marker == 0xDA || marker == 0xD9) break;           // start of scan / end: no more metadata
            int len = (b[i + 2] << 8) | b[i + 3];
            if (len < 2 || i + 2 + len > b.Length) break;
            if (marker == 0xE1 && len > 8 && Encoding.ASCII.GetString(b, i + 4, 4) == "Exif")
                return Parse(b.AsSpan(i + 10, len - 8).ToArray(), name);
            i += 2 + len;
        }
        return new PhotoTag(name, null, null, null, null, null);
    }

    private static PhotoTag Parse(byte[] t, string name)
    {
        bool le = t.Length >= 8 && t[0] == 'I' && t[1] == 'I';
        int U16(int o) => o + 2 > t.Length ? 0 : le ? t[o] | t[o + 1] << 8 : t[o] << 8 | t[o + 1];
        uint U32(int o) => o + 4 > t.Length ? 0 : le ? (uint)(t[o] | t[o + 1] << 8 | t[o + 2] << 16 | t[o + 3] << 24) : (uint)(t[o] << 24 | t[o + 1] << 16 | t[o + 2] << 8 | t[o + 3]);
        Dictionary<int, (int Type, int Count, int ValueAt)> Ifd(int at)
        {
            var d = new Dictionary<int, (int, int, int)>();
            if (at <= 0 || at + 2 > t.Length) return d;
            int n = U16(at);
            for (int k = 0; k < n && at + 2 + 12 * k + 12 <= t.Length; k++)
            {
                int e = at + 2 + 12 * k, type = U16(e + 2), count = (int)U32(e + 4);
                int size = type switch { 1 or 2 or 7 => 1, 3 => 2, 4 or 9 => 4, 5 or 10 => 8, _ => 1 } * count;
                d[U16(e)] = (type, count, size <= 4 ? e + 8 : (int)U32(e + 8));
            }
            return d;
        }
        double Rational(int o) { uint num = U32(o), den = U32(o + 4); return den == 0 ? double.NaN : (double)num / den; }
        string Ascii((int Type, int Count, int ValueAt) e) => e.ValueAt + e.Count <= t.Length ? Encoding.ASCII.GetString(t, e.ValueAt, e.Count).TrimEnd('\0', ' ') : "";

        var ifd0 = Ifd((int)U32(4));
        double? lat = null, lon = null, alt = null, err = null;
        DateTime? when = null;
        if (ifd0.TryGetValue(0x8825, out var gp))
        {
            var g = Ifd((int)U32(gp.ValueAt));
            double? Dms(int tag, int refTag, char neg)
            {
                if (!g.TryGetValue(tag, out var e) || e.Count < 3) return null;
                double v = Rational(e.ValueAt) + Rational(e.ValueAt + 8) / 60 + Rational(e.ValueAt + 16) / 3600;
                if (!double.IsFinite(v)) return null;
                return g.TryGetValue(refTag, out var r) && Ascii(r).StartsWith(neg) ? -v : v;
            }
            lat = Dms(2, 1, 'S');
            lon = Dms(4, 3, 'W');
            if (g.TryGetValue(6, out var a)) { alt = Rational(a.ValueAt); if (g.TryGetValue(5, out var ar) && t[ar.ValueAt] == 1) alt = -alt; }
            if (g.TryGetValue(0x1F, out var he)) err = Rational(he.ValueAt);
        }
        if (ifd0.TryGetValue(0x8769, out var ep) && Ifd((int)U32(ep.ValueAt)).TryGetValue(0x9003, out var dto)
            && DateTime.TryParseExact(Ascii(dto), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) when = dt;
        else if (ifd0.TryGetValue(0x0132, out var d0) && DateTime.TryParseExact(Ascii(d0), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt0)) when = dt0;
        return new PhotoTag(name, lat, lon, alt is { } x && double.IsFinite(x) ? x : null, when, err is { } y && double.IsFinite(y) ? y : null);
    }

    /// <summary>A minimal JPEG carrying only an EXIF block with GPS and time (used by tests and the sample).</summary>
    public static byte[] Fake(double lat, double lon, DateTime taken, double? altitudeM = null)
    {
        var tiff = new List<byte>();
        void W16(int v) { tiff.Add((byte)(v >> 8)); tiff.Add((byte)v); }
        void W32(long v) { tiff.Add((byte)(v >> 24)); tiff.Add((byte)(v >> 16)); tiff.Add((byte)(v >> 8)); tiff.Add((byte)v); }
        // Layout: header(8) | IFD0 at 8: 2 entries | GPS IFD | Exif IFD | data
        const int ifd0 = 8, ifd0Size = 2 + 2 * 12 + 4;
        int gps = ifd0 + ifd0Size, gpsN = altitudeM is null ? 4 : 6, gpsSize = 2 + gpsN * 12 + 4;
        int exif = gps + gpsSize, exifSize = 2 + 12 + 4;
        int data = exif + exifSize;
        int latAt = data, lonAt = data + 24, altAt = data + 48, dtAt = data + 56;
        tiff.AddRange("MM"u8.ToArray()); W16(42); W32(ifd0);
        W16(2);
        W16(0x8769); W16(4); W32(1); W32(exif);
        W16(0x8825); W16(4); W32(1); W32(gps);
        W32(0);
        W16(gpsN);
        W16(1); W16(2); W32(2); tiff.Add((byte)(lat < 0 ? 'S' : 'N')); tiff.AddRange([0, 0, 0]);
        W16(2); W16(5); W32(3); W32(latAt);
        W16(3); W16(2); W32(2); tiff.Add((byte)(lon < 0 ? 'W' : 'E')); tiff.AddRange([0, 0, 0]);
        W16(4); W16(5); W32(3); W32(lonAt);
        if (altitudeM is not null) { W16(5); W16(1); W32(1); tiff.AddRange([0, 0, 0, 0]); W16(6); W16(5); W32(1); W32(altAt); }
        W32(0);
        W16(1); W16(0x9003); W16(2); W32(20); W32(dtAt); W32(0);
        void Dms(double v)
        {
            v = Math.Abs(v);
            int d = (int)v; int m = (int)((v - d) * 60); double s = ((v - d) * 60 - m) * 60;
            W32(d); W32(1); W32(m); W32(1); W32((long)Math.Round(s * 10000)); W32(10000);
        }
        Dms(lat); Dms(lon);
        W32((long)Math.Round((altitudeM ?? 0) * 100)); W32(100);
        tiff.AddRange(Encoding.ASCII.GetBytes(taken.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture) + "\0"));
        var app1 = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 };
        int len = 2 + 6 + tiff.Count;
        app1.Add((byte)(len >> 8)); app1.Add((byte)len);
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);
        app1.AddRange([0xFF, 0xD9]);
        return [.. app1];
    }
}
