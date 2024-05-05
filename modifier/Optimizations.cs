using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Schneegans.Unattend;

public interface IProcessAuditSettings;

public record class EnabledProcessAuditSettings(
  bool IncludeCommandLine
) : IProcessAuditSettings;

public class DisabledProcessAuditSettings : IProcessAuditSettings;

class OptimizationsModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);

    if (Configuration.DisableDefender)
    {
      // https://lazyadmin.nl/win-11/turn-off-windows-defender-windows-11-permanently/
      string filename = @"%TEMP%\disable-defender.ini";
      StringWriter sw = new();
      foreach (string service in (string[])[
        "Sense",
        "WdBoot",
        "WdFilter",
        "WdNisDrv",
        "WdNisSvc",
        "WinDefend",
      ])
      {
        sw.WriteLine($"""
          HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{service}
              "Start" = REG_DWORD 4
          """);
      }
      AddTextFile(sw.ToString(), filename);
      appender.Append(
        CommandBuilder.Raw(@$"regini.exe ""{filename}""")
      );
    }

    if (Configuration.EnableLongPaths)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\FileSystem"" /v LongPathsEnabled /t REG_DWORD /d 1 /f")
      );
    }

    if (Configuration.EnableRemoteDesktop)
    {
      appender.Append([
        CommandBuilder.Raw(@"netsh.exe advfirewall firewall set rule group=""Remote Desktop"" new enable=Yes"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server"" /v fDenyTSConnections /t REG_DWORD /d 0 /f"),
      ]);
    }

    if (Configuration.HardenSystemDriveAcl)
    {
      appender.Append(
        CommandBuilder.Raw(@"icacls.exe C:\ /remove:g ""*S-1-5-11""") // Group »Authenticated Users«
      );
    }

    {
      if (Configuration.ProcessAuditSettings is EnabledProcessAuditSettings settings)
      {
        appender.Append(
          CommandBuilder.Raw(@"auditpol.exe /set /subcategory:""Process Creation"" /success:enable /failure:enable")
        );
        if (settings.IncludeCommandLine)
        {
          appender.Append(
            CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit"" /v ProcessCreationIncludeCmdLine_Enabled /t REG_DWORD /d 1 /f")
          );
        }
      }
    }

    if (Configuration.AllowPowerShellScripts)
    {
      appender.Append(
        CommandBuilder.PowerShellCommand(@"Set-ExecutionPolicy -Scope 'LocalMachine' -ExecutionPolicy 'RemoteSigned' -Force;")
      );
    }

    if (Configuration.DisableLastAccess)
    {
      appender.Append(
        CommandBuilder.Raw(@"fsutil.exe behavior set disableLastAccess 1")
      );
    }

    if (Configuration.NoAutoRebootWithLoggedOnUsers)
    {
      appender.Append([
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"" /v AUOptions /t REG_DWORD /d 4 /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"" /v NoAutoRebootWithLoggedOnUsers /t REG_DWORD /d 1 /f"),
      ]);
    }

    if (Configuration.DisableSystemRestore)
    {
      CommandAppender oobe = GetAppender(CommandConfig.Oobe);
      oobe.Append(
        CommandBuilder.PowerShellCommand(@"Disable-ComputerRestore -Drive 'C:\';")
      );
    }

    if (Configuration.DisableWidgets)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Dsh"" /v AllowNewsAndInterests /t REG_DWORD /d 0 /f")
      );
    }

    if (Configuration.TurnOffSystemSounds)
    {
      appender.Append(
        CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          string script = $$"""
            New-PSDrive -PSProvider 'Registry' -Root 'HKEY_USERS' -Name 'HKU';
            $excludes = Get-ChildItem -LiteralPath 'HKU:\{{subKey}}\AppEvents\EventLabels' |
              Where-Object -FilterScript { ($_ | Get-ItemProperty).ExcludeFromCPL -eq 1; } |
              Select-Object -ExpandProperty 'PSChildName';
            Get-ChildItem -Path 'HKU:\{{subKey}}\AppEvents\Schemes\Apps\*\*' |
              Where-Object -Property 'PSChildName' -NotIn $excludes |
              Get-ChildItem -Include '.Current' | Set-ItemProperty -Name '(default)' -Value '';
            Remove-PSDrive -Name 'HKU';
            """;
          string ps1File = @"%TEMP%\sounds.ps1";
          AddTextFile(script, ps1File);
          return [
            CommandBuilder.InvokePowerShellScript(ps1File),
            CommandBuilder.UserRunOnceCommand("NoSounds", @"C:\Windows\System32\reg.exe add ""HKCU\AppEvents\Schemes"" /ve /t REG_SZ /d "".None"" /f", rootKey, subKey),
          ];
        }));
      appender.Append([
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation"" /v DisableStartupSound /t REG_DWORD /d 1 /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\EditionOverrides"" /v UserSetting_DisableStartupSound /t REG_DWORD /d 1 /f"),
      ]);
    }

    if (Configuration.RunScriptOnFirstLogon)
    {
      appender.Append(
        CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          return [CommandBuilder.UserRunOnceCommand("UserFirstLogon", Constants.FirstLogonScript, rootKey, subKey)];
        }
      ));
    }

    if (Configuration.DisableAppSuggestions)
    {
      // https://skanthak.homepage.t-online.de/ten.html#eighth

      appender.Append(
        CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          return new List<string>()
          {
            "ContentDeliveryAllowed",
            "FeatureManagementEnabled",
            "OEMPreInstalledAppsEnabled",
            "PreInstalledAppsEnabled",
            "PreInstalledAppsEverEnabled",
            "SilentInstalledAppsEnabled",
            "SoftLandingEnabled",
            "SubscribedContentEnabled",
            "SubscribedContent-310093Enabled",
            "SubscribedContent-338387Enabled",
            "SubscribedContent-338388Enabled",
            "SubscribedContent-338389Enabled",
            "SubscribedContent-338393Enabled",
            "SubscribedContent-353698Enabled",
            "SystemPaneSuggestionsEnabled",
          }.Select(value =>
          {
            return CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v ""{value}"" /t REG_DWORD /d 0 /f");
          });
        })
      );

      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\Software\Policies\Microsoft\Windows\CloudContent"" /v ""DisableWindowsConsumerFeatures"" /t REG_DWORD /d 0 /f")
      );
    }

    if (Configuration.VBoxGuestAdditions)
    {
      string ps1File = @"%TEMP%\VBoxGuestAdditions.ps1";
      string script = Util.StringFromResource("VBoxGuestAdditions.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }
  }
}
