using System.Reflection;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

class BuildModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() is AssemblyInformationalVersionAttribute attr)
    {
      if (Regex.Match(attr.InformationalVersion, @"\+([a-f0-9]{40})$", RegexOptions.IgnoreCase) is Match match && match.Success)
      {
        string hash = match.Groups[1].Value;
        Document.SelectSingleNodeOrThrow("//s:Build/s:Commit/s:Hash", NamespaceManager).InnerText = hash;
        Document.SelectSingleNodeOrThrow("//s:Build/s:Commit/s:GitHubUrl", NamespaceManager).InnerText = $"https://github.com/cschneegans/unattend-generator/commit/{hash}";
      }
    }
  }
}