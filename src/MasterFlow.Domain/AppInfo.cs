using System.Reflection;

namespace MasterFlow.Domain;

/// <summary>Product version, taken from &lt;Version&gt; in Directory.Build.props (the single place to change it).</summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
