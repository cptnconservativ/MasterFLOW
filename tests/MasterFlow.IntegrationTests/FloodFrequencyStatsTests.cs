using MasterFlow.Application.Hydrology;

namespace MasterFlow.IntegrationTests;

/// <summary>Distributions, EMA and the Multiple Grubbs–Beck test behind the Bulletin 17C tool.</summary>
public class FloodFrequencyStatsTests
{
    [Theory]
    [InlineData(0.975, 1.959963985)]
    [InlineData(0.99, 2.326347874)]
    [InlineData(0.001, -3.090232306)]
    [InlineData(0.5, 0.0)]
    public void NormInv_MatchesTables(double p, double z) => Assert.Equal(z, Stats.NormInv(p), 8);

    [Fact]
    public void NormCdf_FarTail_KeepsRelativeAccuracy()
    {
        Assert.Equal(2.866515719e-7, Stats.NormCdf(-5), 15);
        Assert.Equal(1.0, Stats.NormCdf(-8) / 6.220960574e-16, 6);
    }

    [Fact]
    public void Gamma_RoundTrips()
    {
        foreach (double a in new[] { 0.5, 2.0, 16.0, 400.0, 20000.0 })
            foreach (double p in new[] { 0.002, 0.1, 0.5, 0.9, 0.998 })
                Assert.Equal(p, Stats.GammaP(a, Stats.GammaPInv(a, p)), 10);
        Assert.Equal(1 - Math.Exp(-2), Stats.GammaP(1, 2), 12);
        Assert.Equal(Math.Log(24), Stats.LogGamma(5), 12);
    }

    [Fact]
    public void NoncentralT_ReducesToKnownCases()
    {
        // Central t, ν = 10: P(T ≤ 2.228) = 0.975.
        Assert.Equal(0.975, Stats.NoncentralTCdf(2.228139, 10, 0), 5);
        // Very large ν: Φ(t − δ).
        Assert.Equal(Stats.NormCdf(0.7), Stats.NoncentralTCdf(1.5, 1e5, 0.8), 3);
    }

    [Theory]   // Bulletin 17B Appendix 3 frequency factors
    [InlineData(0.0, 0.99, 2.32635)]
    [InlineData(1.0, 0.99, 3.02256)]
    [InlineData(-1.0, 0.99, 1.58838)]
    [InlineData(0.5, 0.99, 2.68572)]
    [InlineData(-0.5, 0.99, 1.95472)]
    [InlineData(2.0, 0.99, 3.60517)]
    [InlineData(1.0, 0.5, -0.16397)]
    [InlineData(-1.0, 0.5, 0.16397)]
    public void FrequencyFactor_MatchesBulletin17B(double g, double p, double k) => Assert.Equal(k, Pearson3.K(g, p), 0.00006);

    [Theory]
    [InlineData(0.7, -1.0, 0.5)]
    [InlineData(-0.8, -0.3, double.PositiveInfinity)]
    [InlineData(-0.8, double.NegativeInfinity, -0.6)]
    [InlineData(1.4, 0.2, 2.5)]
    [InlineData(0.004, -0.5, 1.0)]
    public void TruncatedMoments_MatchNumericalIntegration(double g, double zlo, double zhi)
    {
        var d = new Pearson3(3.0, 0.25, g);
        var (pr, m1, m2, m3) = d.TruncatedMoments(3.0 + 0.25 * zlo, 3.0 + 0.25 * zhi);
        // Integrate z^k·f(z) numerically from the CDF on a fine grid.
        double a = double.IsNegativeInfinity(zlo) ? -12 : zlo, b = double.IsPositiveInfinity(zhi) ? 12 : zhi;
        int n = 40000;
        double h = (b - a) / n, s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        double prev = d.Cdf(3.0 + 0.25 * a);
        for (int i = 1; i <= n; i++)
        {
            double z = a + (i - 0.5) * h, c = d.Cdf(3.0 + 0.25 * (a + i * h)), dp = c - prev;
            prev = c;
            s0 += dp; s1 += z * dp; s2 += z * z * dp; s3 += z * z * z * dp;
        }
        Assert.Equal(s0, pr, 6);
        Assert.Equal(s1 / s0, m1, 4);
        Assert.Equal(s2 / s0, m2, 4);
        Assert.Equal(s3 / s0, m3, 3);
    }

    [Fact]
    public void Ema_ExactDataOnly_GivesSampleMoments()
    {
        double[] x = [3.1, 3.4, 2.9, 3.8, 3.3, 3.05, 3.6, 2.7, 3.25, 3.5, 3.9, 3.15];
        var s = Ema.SampleMoments(x);
        var e = Ema.Fit(x.Select(v => new LogInterval(v, v)).ToList());
        Assert.Equal(s.Mean, e.Mean, 9);
        Assert.Equal(s.Sd, e.Sd, 9);
        Assert.Equal(s.Skew, e.Skew, 8);
    }

    [Fact]
    public void Ema_CensoredLowValues_RecoversTheDistribution()
    {
        // 2000 values at the quantiles of a known LP3 (mean 3, sd 0.3, skew −0.4); the lowest 25 % become "less than"
        // intervals. EMA fills the censored part from the fitted distribution, so it lands on the parent distribution.
        var truth = new Pearson3(3.0, 0.3, -0.4);
        var x = Enumerable.Range(1, 2000).Select(i => truth.Quantile(i / 2001.0)).ToList();
        double t = x[499];
        var obs = x.Select(v => v < t ? new LogInterval(double.NegativeInfinity, t) : new LogInterval(v, v)).ToList();
        var e = Ema.Fit(obs);
        Assert.Equal(3.0, e.Mean, 0.005);
        Assert.Equal(0.3, e.Sd, 0.005);
        Assert.Equal(-0.4, e.Skew, 0.03);
        // Fixing the skew changes only the mean and sd estimates.
        var f = Ema.Fit(obs, -0.4);
        Assert.Equal(-0.4, f.Skew);
        Assert.Equal(3.0, f.Mean, 1);
    }

    [Fact]
    public void SkewMse_MatchesBulletin17B()
    {
        // G = 0, n = 10: A = −0.33, B = 0.94 → MSE = 10^−0.33.
        Assert.Equal(Math.Pow(10, -0.33), Ema.SkewMse(0, 10), 9);
        Assert.Equal(Math.Pow(10, -0.33 + 0.08 * 0.5 - (0.94 - 0.26 * 0.5) * Math.Log10(5)), Ema.SkewMse(-0.5, 50), 9);
        Assert.Equal(Math.Pow(10, -0.52 + 0.30 * 2 - 0.55 * Math.Log10(3)), Ema.SkewMse(2, 30), 9);
        Assert.Equal((0.302 * 0.4 + 0.1 * -0.2) / 0.402, Ema.WeightedSkew(0.4, 0.1, -0.2, 0.302), 12);
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(20, 5)]
    [InlineData(40, 3)]
    public void MgbtPValue_MatchesMonteCarlo(int n, int r)
    {
        var rng = new Random(17);
        const int sims = 20000;
        var w = new double[sims];
        var x = new double[n];
        for (int s = 0; s < sims; s++)
        {
            for (int i = 0; i < n; i++)
            {
                double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
                x[i] = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }
            Array.Sort(x);
            var rest = x.Skip(r).ToArray();
            double m = rest.Average(), sd = Math.Sqrt(rest.Sum(v => (v - m) * (v - m)) / (rest.Length - 1));
            w[s] = (x[r - 1] - m) / sd;
        }
        Array.Sort(w);
        foreach (double q in new[] { 0.01, 0.05, 0.10, 0.30 })
            Assert.InRange(Mgbt.PValue(n, r, w[(int)(q * sims)]), q - 0.012 - 0.1 * q, q + 0.012 + 0.1 * q);
    }

    [Fact]
    public void Mgbt_FlagsObviousLowOutliers_AndNotACleanSample()
    {
        var clean = Enumerable.Range(1, 30).Select(i => Stats.NormInv(i / 31.0) * 0.25 + 3).ToList();
        Assert.Equal(0, Mgbt.Test(clean).Klow);
        var dirty = clean.Skip(3).Concat([0.3, 0.9, 1.2]).ToList();
        Assert.Equal(3, Mgbt.Test(dirty).Klow);
    }
}
