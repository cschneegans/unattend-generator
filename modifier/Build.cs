using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

class BuildModifier(ModifierContext context) : Modifier(context)
{
  private static string GetRepositoryUrl()
  {
    var metadataAttrs = typeof(BuildModifier).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
    var repoMeta = metadataAttrs.FirstOrDefault(a => a.Key == "RepositoryUrl");
    if (!string.IsNullOrWhiteSpace(repoMeta?.Value))
    {
      return repoMeta.Value.TrimEnd('/');
    }

    try
    {
      var psi = new ProcessStartInfo("git", "config --get remote.origin.url")
      {
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      using var proc = System.Diagnostics.Process.Start(psi);
      if (proc != null)
      {
        string output = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
          string url = output;
          if (url.StartsWith("git@github.com:"))
          {
            url = "https://github.com/" + url.Substring("git@github.com:".Length);
          }
          if (url.EndsWith(".git"))
          {
            url = url.Substring(0, url.Length - 4);
          }
          return url.TrimEnd('/');
        }
      }
    }
    catch
    {
    }

    return "https://github.com/cschneegans/unattend-generator";
  }

  public override void Process()
  {
    if (GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() is AssemblyInformationalVersionAttribute attr)
    {
      if (Regex.Match(attr.InformationalVersion, @"\+([a-f0-9]{40})$", RegexOptions.IgnoreCase) is Match match && match.Success)
      {
        string hash = match.Groups[1].Value;
        string repoUrl = GetRepositoryUrl();
        Document.SelectSingleNodeOrThrow("//s:Build/s:Commit/s:Hash", NamespaceManager).InnerText = hash;
        Document.SelectSingleNodeOrThrow("//s:Build/s:Commit/s:GitHubUrl", NamespaceManager).InnerText = $"{repoUrl}/commit/{hash}";
      }
    }
  }
}