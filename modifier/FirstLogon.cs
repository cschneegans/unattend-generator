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
    string ps1File = EmbedTextFile("FirstLogon.ps1", FirstLogonScript.GetScript());
    appender.Append(CommandBuilder.InvokePowerShellScript(ps1File));
  }
}