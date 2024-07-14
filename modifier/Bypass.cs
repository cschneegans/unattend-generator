namespace Schneegans.Unattend;

class BypassModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.BypassRequirementsCheck)
    {
      CommandAppender appender = GetAppender(CommandConfig.WindowsPE);

      string[] values = [
        "BypassTPMCheck",
        "BypassSecureBootCheck",
        "BypassRAMCheck"
      ];

      foreach (string value in values)
      {
        appender.Append(
          CommandBuilder.RegistryCommand(@$"add ""HKLM\SYSTEM\Setup\LabConfig"" /v {value} /t REG_DWORD /d 1 /f")
        );
      }
    }

    if (Configuration.BypassNetworkCheck)
    {
      CommandAppender appender = GetAppender(CommandConfig.Specialize);
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE"" /v BypassNRO /t REG_DWORD /d 1 /f")
      );
    }
  }
}
