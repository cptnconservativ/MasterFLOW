using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasterFlow.Application.Licensing;

/// <summary>
/// The signed content of a license key. <see cref="Features"/> holds premium feature ids (see
/// <see cref="PremiumFeatures"/>) or "*" for every premium feature, present and future.
/// </summary>
public sealed record LicensePayload
{
    [JsonPropertyName("v")] public int Version { get; init; } = 1;
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("licensee")] public string Licensee { get; init; } = "";
    [JsonPropertyName("edition")] public string Edition { get; init; } = "premium";
    [JsonPropertyName("features")] public IReadOnlyList<string> Features { get; init; } = ["*"];
    [JsonPropertyName("seats")] public int Seats { get; init; } = 1;
    /// <summary>ISO date (yyyy-MM-dd) the key was issued.</summary>
    [JsonPropertyName("issued")] public string Issued { get; init; } = "";
    /// <summary>ISO date (yyyy-MM-dd), the last day the key is valid; null for a perpetual license.</summary>
    [JsonPropertyName("expires")] public string? Expires { get; init; }
}

public enum LicenseStatus { None, Valid, Expired, Invalid }

/// <summary>The checked state of the license the user entered. Only a <see cref="LicenseStatus.Valid"/> license unlocks anything.</summary>
public sealed record LicenseState(LicenseStatus Status, LicensePayload? Payload, string Message)
{
    public static readonly LicenseState Free = new(LicenseStatus.None, null, "No license key entered: MasterFlow Free. Premium tools are listed but do not run.");

    public bool IsPremium => Status == LicenseStatus.Valid;

    /// <summary>True when the license is valid and covers <paramref name="featureId"/> (or every feature).</summary>
    public bool Allows(string featureId) =>
        IsPremium && Payload!.Features.Any(f => f == "*" || string.Equals(f, featureId, StringComparison.OrdinalIgnoreCase));

    /// <summary>One line for the window: edition, licensee and expiry, or why the key was rejected.</summary>
    public string Summary => Status switch
    {
        LicenseStatus.Valid => $"MasterFlow Premium, licensed to {Payload!.Licensee}" + (Payload.Expires is { } e ? $", valid through {e}" : ", perpetual")
                               + (Payload.Features.Contains("*") ? "" : $" ({string.Join(", ", Payload.Features)})") + ".",
        _ => Message,
    };
}

/// <summary>
/// Offline license keys. A key is <c>MF1-</c> + base64url(UTF-8 JSON payload) + "." + base64url(ECDSA P-256 / SHA-256
/// signature of the payload bytes, IEEE P1363 format). The application holds only the public key, so it can check a key
/// with no network access but cannot make one; keys are issued with the private key by the MasterFlow.LicenseTool console
/// program. Nothing is sent anywhere and the key is not tied to a machine (seat counts are a contract term, not enforced).
/// </summary>
public static class LicenseKey
{
    public const string Prefix = "MF1-";

    /// <summary>Public key of the MasterFlow issuer (SubjectPublicKeyInfo, base64). Replace together with the private key if it is ever exposed.</summary>
    public const string IssuerPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEozSX0AxrSDoWUc+4nFrE7RJ8GWi/3q96eegwCeWmIo3xbpftr8pzzPZ2A76DbqsYmieKy1JuIdVnWROMeaHxJQ==";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Checks <paramref name="key"/> against the issuer's public key (or <paramref name="publicKey"/> in tests) on <paramref name="today"/>.</summary>
    public static LicenseState Check(string? key, DateOnly today, string? publicKey = null)
    {
        key = new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (key.Length == 0) return LicenseState.Free;
        if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            return Invalid("The license key does not start with \"MF1-\". Paste the whole key exactly as it was sent.");
        var parts = key[Prefix.Length..].Split('.');
        if (parts.Length != 2) return Invalid("The license key is incomplete (it needs a payload and a signature separated by a dot).");
        byte[] body, sig;
        try { body = FromBase64Url(parts[0]); sig = FromBase64Url(parts[1]); }
        catch (FormatException) { return Invalid("The license key contains characters that are not part of a key."); }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey ?? IssuerPublicKey), out _);
            if (!ecdsa.VerifyData(body, sig, HashAlgorithmName.SHA256))
                return Invalid("The license key's signature does not match: the key was mistyped, altered, or not issued for MasterFlow.");
        }
        catch (CryptographicException) { return Invalid("The license key's signature could not be read."); }

        LicensePayload? p;
        try { p = JsonSerializer.Deserialize<LicensePayload>(body, Json); }
        catch (JsonException) { return Invalid("The license key's content could not be read."); }
        if (p is null || p.Version != 1 || string.IsNullOrWhiteSpace(p.Licensee))
            return Invalid("The license key was made for a different version of MasterFlow.");
        if (p.Expires is { } ex)
        {
            if (!DateOnly.TryParseExact(ex, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
                return Invalid("The license key's expiry date could not be read.");
            if (today > until)
                return new LicenseState(LicenseStatus.Expired, p, $"The license for {p.Licensee} expired on {ex}. Premium tools are locked until a renewed key is entered.");
        }
        return new LicenseState(LicenseStatus.Valid, p, "License key accepted.");
    }

    /// <summary>Makes a key from a payload with the issuer's private key (PKCS#8 PEM). Used by the license tool and tests only.</summary>
    public static string Issue(LicensePayload payload, string privateKeyPem)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var sig = ecdsa.SignData(body, HashAlgorithmName.SHA256);
        return Prefix + ToBase64Url(body) + "." + ToBase64Url(sig);
    }

    /// <summary>A new P-256 key pair: private key as PKCS#8 PEM, public key as SubjectPublicKeyInfo base64.</summary>
    public static (string PrivatePem, string PublicBase64) NewKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    private static LicenseState Invalid(string why) => new(LicenseStatus.Invalid, null, why + " Premium tools stay locked.");

    private static string ToBase64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }

    /// <summary>UTF-8 helper for callers that log the payload.</summary>
    public static string Describe(LicensePayload p) => Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(p, Json));
}
