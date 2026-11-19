using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

/// <summary>Offline license keys and the premium gate. Keys here are signed with a throw-away test key pair.</summary>
public class LicenseTests
{
    private static readonly (string Priv, string Pub) Pair = LicenseKey.NewKeyPair();
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static string Key(string? expires = null, params string[] features) =>
        LicenseKey.Issue(new LicensePayload { Id = "t1", Licensee = "Test Engineering", Issued = "2026-11-19", Expires = expires, Features = features.Length == 0 ? ["*"] : features }, Pair.Priv);

    [Fact]
    public void ValidKey_UnlocksEveryFeature()
    {
        var s = LicenseKey.Check(Key(), Today, Pair.Pub);
        Assert.Equal(LicenseStatus.Valid, s.Status);
        Assert.True(s.IsPremium);
        Assert.All(PremiumFeatures.All, f => Assert.True(s.Allows(f.Id)));
        Assert.Contains("Test Engineering", s.Summary);
        Assert.Contains("perpetual", s.Summary);
    }

    [Fact]
    public void Whitespace_And_LineBreaks_InPastedKey_AreIgnored()
    {
        var k = Key();
        var pasted = "  " + k[..20] + "\r\n" + k[20..] + " \n";
        Assert.Equal(LicenseStatus.Valid, LicenseKey.Check(pasted, Today, Pair.Pub).Status);
    }

    [Fact]
    public void FeatureList_LimitsWhatUnlocks()
    {
        var s = LicenseKey.Check(Key(null, "water-quality"), Today, Pair.Pub);
        Assert.True(s.Allows("water-quality"));
        Assert.False(s.Allows("hydrograph-routing"));
    }

    [Fact]
    public void Expiry_IsInclusiveOfTheLastDay()
    {
        Assert.Equal(LicenseStatus.Valid, LicenseKey.Check(Key("2026-11-19"), Today, Pair.Pub).Status);
        var s = LicenseKey.Check(Key("2026-11-18"), Today, Pair.Pub);
        Assert.Equal(LicenseStatus.Expired, s.Status);
        Assert.False(s.Allows("water-quality"));
        Assert.Contains("expired on 2026-11-18", s.Message);
    }

    [Fact]
    public void AlteredPayload_IsRejected()
    {
        var k = Key("2026-12-31");
        // Re-encode the payload with a later expiry but keep the old signature.
        var parts = k[LicenseKey.Prefix.Length..].Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[0]))).Replace("2026-12-31", "2099-12-31");
        var forged = LicenseKey.Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + parts[1];
        var s = LicenseKey.Check(forged, Today, Pair.Pub);
        Assert.Equal(LicenseStatus.Invalid, s.Status);
        Assert.False(s.IsPremium);
    }

    [Fact]
    public void KeyFromAnotherIssuer_IsRejected()
    {
        var other = LicenseKey.NewKeyPair();
        var k = LicenseKey.Issue(new LicensePayload { Licensee = "Pirate" }, other.PrivatePem);
        Assert.Equal(LicenseStatus.Invalid, LicenseKey.Check(k, Today, Pair.Pub).Status);
        // ...and a test key is not accepted by the real issuer key built into the application.
        Assert.Equal(LicenseStatus.Invalid, LicenseKey.Check(Key(), Today).Status);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("MF1-abc")]
    [InlineData("MF1-***.***")]
    [InlineData("MF1-e30.AAAA")]
    public void Garbage_IsInvalid_NotAnException(string key) =>
        Assert.Equal(LicenseStatus.Invalid, LicenseKey.Check(key, Today, Pair.Pub).Status);

    [Fact]
    public void EmptyKey_IsFree() => Assert.Equal(LicenseStatus.None, LicenseKey.Check("  ", Today, Pair.Pub).Status);

    private sealed class FakePremiumTool : IDesignTool
    {
        public string Id => "water-quality";
        public string Name => "Fake WQ";
        public string Description => "";
        public bool UsesNetwork => false;
        public string SampleInput(DesignToolContext context) => "";
        public ToolResult Run(string input, DesignToolContext context) => new("ran", ["ok"], [], []);
    }

    [Fact]
    public void Gate_BlocksPremiumTool_WithoutLicense_AndRunsItWithOne()
    {
        var tool = new FakePremiumTool();
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        var blocked = PremiumFeatures.Run(tool, "", ctx, LicenseState.Free);
        Assert.False(blocked.Succeeded);
        Assert.Contains(blocked.Messages, m => m.Code == "E-LIC-001");
        Assert.Empty(blocked.Tables);

        var expired = PremiumFeatures.Run(tool, "", ctx, LicenseKey.Check(Key("2020-01-01"), Today, Pair.Pub));
        Assert.Contains("expired", expired.Messages.Single().Text);

        var partial = PremiumFeatures.Run(tool, "", ctx, LicenseKey.Check(Key(null, "hydrograph-routing"), Today, Pair.Pub));
        Assert.Contains("does not include", partial.Messages.Single().Text);

        Assert.Equal("ran", PremiumFeatures.Run(tool, "", ctx, LicenseKey.Check(Key(), Today, Pair.Pub)).Title);
    }

    [Fact]
    public void FreeTools_RunWithoutLicense()
    {
        foreach (var t in DesignTools.All.Where(t => !PremiumFeatures.IsPremium(t)))
        {
            var ctx = new DesignToolContext(UnitSystem.USCustomary);
            var r = PremiumFeatures.Run(t, t.SampleInput(ctx), ctx, LicenseState.Free);
            Assert.DoesNotContain(r.Messages, m => m.Code == "E-LIC-001");
        }
        Assert.Equal("Rational method", PremiumFeatures.Label(new NamedTool("rational-rapid-city", "Rational method")));
        Assert.EndsWith("★ Premium", PremiumFeatures.Label(new NamedTool("hydrograph-routing", "Routing")));
    }

    private sealed record NamedTool(string Id, string Name) : IDesignTool
    {
        public string Description => "";
        public bool UsesNetwork => false;
        public string SampleInput(DesignToolContext context) => "";
        public ToolResult Run(string input, DesignToolContext context) => ToolResult.Failed(Name, CalcMessage.Error("x", "x"));
    }

    private static string Pad(string s) { s = s.Replace('-', '+').Replace('_', '/'); return s + new string('=', (4 - s.Length % 4) % 4); }
}
