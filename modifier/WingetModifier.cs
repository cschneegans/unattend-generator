using System.Linq;

namespace Schneegans.Unattend;

public class WingetModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.Winget.Packages.Count > 0)
    {
      var commands = Configuration.Winget.Packages.Select(p =>
      {
        return $"winget install --id {p} --accept-package-agreements --accept-source-agreements;";
      });
      FirstLogonScript.Append(
        string.Join("\r\n", commands)
      );
    }
  }
}
