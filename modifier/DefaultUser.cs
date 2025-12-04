namespace Schneegans.Unattend;

class DefaultUserModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (DefaultUserScript.IsEmpty)
    {
      return;
    }
    CommandAppender appender = GetAppender(CommandConfig.Specialize);
    string ps1File = EmbedTextFile("DefaultUser.ps1", DefaultUserScript.GetScript());
    appender.Append(CommandBuilder.RegistryCommand(@"load ""HKU\DefaultUser"" ""C:\Users\Default\NTUSER.DAT"""));
    appender.Append(CommandBuilder.InvokePowerShellScript(ps1File));
    appender.Append(CommandBuilder.RegistryCommand(@"unload ""HKU\DefaultUser"""));
  }
}