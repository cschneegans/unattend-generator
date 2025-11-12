namespace Schneegans.Unattend;

class AccessibilityModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.UseNarrator)
    {
      GetAppender(CommandConfig.WindowsPE).Append(
        CommandBuilder.ShellCommand(@"start X:\Windows\System32\Narrator.exe")
      );
      SpecializeScript.Append("""
        & 'C:\Windows\System32\Narrator.exe';
        reg.exe ADD "HKLM\Software\Microsoft\Windows NT\CurrentVersion\Accessibility" /v Configuration /t REG_SZ /d narrator /f;
        """);
      UserOnceScript.Append("""
        & 'C:\Windows\System32\Narrator.exe';
        reg.exe ADD "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Accessibility" /v Configuration /t REG_SZ /d narrator /f;
        """);
    }
  }
}