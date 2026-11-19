using MasterFlow.Application.Licensing;

// MasterFlow license issuer. Not shipped with the application.
//   keygen <folder>                         new issuer key pair (private PEM + public base64)
//   issue <private.pem> <licensee> [--expires yyyy-MM-dd] [--features a,b|*] [--seats n] [--id text]
//   check <key>                             check a key against the issuer public key built into MasterFlow
static int Usage()
{
    Console.Error.WriteLine("usage:\n  keygen <folder>\n  issue <private.pem> <licensee> [--expires yyyy-MM-dd] [--features id,id|*] [--seats n] [--id text]\n  check <key>");
    Console.Error.WriteLine("features: * or " + string.Join(", ", PremiumFeatures.All.Select(f => f.Id)));
    return 2;
}

if (args.Length == 0) return Usage();
switch (args[0])
{
    case "keygen" when args.Length == 2:
    {
        var (priv, pub) = LicenseKey.NewKeyPair();
        Directory.CreateDirectory(args[1]);
        File.WriteAllText(Path.Combine(args[1], "masterflow-issuer-private-key.pem"), priv);
        File.WriteAllText(Path.Combine(args[1], "masterflow-issuer-public-key.txt"), pub);
        Console.WriteLine("Public key (put it in LicenseKey.IssuerPublicKey; every key issued with the old private key stops working):");
        Console.WriteLine(pub);
        return 0;
    }
    case "issue" when args.Length >= 3:
    {
        string? expires = null, id = null; int seats = 1; IReadOnlyList<string> features = ["*"];
        for (int i = 3; i + 1 < args.Length; i += 2)
            switch (args[i])
            {
                case "--expires": expires = args[i + 1]; break;
                case "--features": features = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--seats": seats = int.Parse(args[i + 1]); break;
                case "--id": id = args[i + 1]; break;
                default: return Usage();
            }
        var unknown = features.Where(f => f != "*" && PremiumFeatures.All.All(p => p.Id != f)).ToList();
        if (unknown.Count > 0) { Console.Error.WriteLine("unknown feature(s): " + string.Join(", ", unknown)); return Usage(); }
        var payload = new LicensePayload
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..12], Licensee = args[2], Features = features, Seats = seats,
            Issued = DateTime.UtcNow.ToString("yyyy-MM-dd"), Expires = expires,
        };
        var key = LicenseKey.Issue(payload, File.ReadAllText(args[1]));
        var check = LicenseKey.Check(key, DateOnly.FromDateTime(DateTime.Now));
        Console.Error.WriteLine(check.Summary);
        Console.WriteLine(key);
        return check.Status == LicenseStatus.Valid ? 0 : 1;
    }
    case "check" when args.Length == 2:
    {
        var s = LicenseKey.Check(args[1], DateOnly.FromDateTime(DateTime.Now));
        Console.WriteLine(s.Summary);
        return s.Status == LicenseStatus.Valid ? 0 : 1;
    }
    default: return Usage();
}
