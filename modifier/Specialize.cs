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
    string ps1File = EmbedTextFile("Specialize.ps1", SpecializeScript.GetScript());
    appender.Append(CommandBuilder.InvokePowerShellScript(ps1File));
  }
}