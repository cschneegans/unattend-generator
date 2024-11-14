namespace Schneegans.Unattend;

class UserOnceModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (UserOnceScript.IsEmpty)
    {
      return;
    }

    string script = UserOnceScript.GetScript();
    string ps1File = @"C:\Windows\Setup\Scripts\UserOnce.ps1";
    AddTextFile(script, ps1File);
    static string Escape(string s)
    {
      return s.Replace(@"""", @"\""""");
    }
    string command = Escape(CommandBuilder.InvokePowerShellScript(ps1File));
    DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\RunOnce"" /v ""UnattendedSetup"" /t REG_SZ /d ""{command}"" /f;");
  }
}