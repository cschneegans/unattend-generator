namespace Schneegans.Unattend;

class SpecializeModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (SpecializeScript.IsEmpty)
    {
      return;
    }
    CommandAppender appender = GetAppender(CommandConfig.Specialize);
    string script = SpecializeScript.GetScript();
    string ps1File = @"C:\Windows\Setup\Scripts\Specialize.ps1";
    AddTextFile(script, ps1File);
    appender.Append(CommandBuilder.InvokePowerShellScript(ps1File));
  }
}