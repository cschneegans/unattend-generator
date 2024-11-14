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
    string script = DefaultUserScript.GetScript();
    string ps1File = @"C:\Windows\Setup\Scripts\DefaultUser.ps1";
    AddTextFile(script, ps1File);

    appender.Append(CommandBuilder.RegistryCommand(@"load ""HKU\DefaultUser"" ""C:\Users\Default\NTUSER.DAT"""));
    appender.Append(CommandBuilder.InvokePowerShellScript(ps1File));
    appender.Append(CommandBuilder.RegistryCommand(@"unload ""HKU\DefaultUser"""));
  }
}