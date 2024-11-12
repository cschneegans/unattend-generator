namespace Schneegans.Unattend;

class FirstLogonModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (FirstLogonScript.IsEmpty)
    {
      return;
    }
    CommandAppender appender = GetAppender(CommandConfig.Oobe);
    string script = FirstLogonScript.GetScript();
    string ps1File = @"C:\Windows\Setup\Scripts\FirstLogon.ps1";
    AddTextFile(script, ps1File);
    appender.Append(CommandBuilder.InvokePowerShellScript(ps1File));
  }
}