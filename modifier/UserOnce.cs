namespace Schneegans.Unattend;

class UserOnceModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);
    string script = UserOnceScript.GetScript();
    string ps1File = @"C:\Windows\Setup\Scripts\UserOnce.ps1";
    AddTextFile(script, ps1File);

    appender.Append(
      CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
      {
        static string Escape(string s)
        {
          return s.Replace(@"""", @"\""");
        }

        string command = CommandBuilder.InvokePowerShellScript(ps1File);
        return [CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\RunOnce"" /v ""UnattendedSetup"" /t REG_SZ /d ""{Escape(command)}"" /f")];
      })
    );
  }
}