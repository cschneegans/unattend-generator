namespace Schneegans.Unattend;

class UserOnceModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (UserOnceScript.IsEmpty)
    {
      return;
    }

    string ps1File = EmbedTextFile("UserOnce.ps1", UserOnceScript.GetScript());
    static string Escape(string s)
    {
      return s.Replace(@"""", @"\""""");
    }
    string command = Escape(CommandBuilder.InvokePowerShellScript(ps1File));
    DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\RunOnce"" /v ""UnattendedSetup"" /t REG_SZ /d ""{command}"" /f;");
  }
}