namespace Schneegans.Unattend;

class DeleteModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.KeepSensitiveFiles)
    {
      return;
    }

    if (Configuration.AccountSettings is UnattendedAccountSettings settings)
    {
      if (settings.AutoLogonSettings is NoneAutoLogonSettings)
      {
        throw new ConfigurationException("To delete sensitive files, you must let Windows log on to an administrator account.");
      }
    }

    FirstLogonScript.Append("""
      Remove-Item -LiteralPath @(
        'C:\Windows\Panther\unattend.xml';
        'C:\Windows\Panther\unattend-original.xml';
        'C:\Windows\Setup\Scripts\Wifi.xml';
      ) -Force -ErrorAction 'SilentlyContinue' -Verbose;
      """);
  }
}