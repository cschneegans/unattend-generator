using System.Collections.Generic;
using System.Linq;

namespace Schneegans.Unattend;

public interface IProcessAuditSettings;

public record class EnabledProcessAuditSettings(
  bool IncludeCommandLine
) : IProcessAuditSettings;

public enum HideModes
{
  None, HiddenSystem, Hidden
}

public class DisabledProcessAuditSettings : IProcessAuditSettings;

class OptimizationsModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);

    {
      IEnumerable<string> SetExplorerOptions(string rootKey, string subKey)
      {
        if (Configuration.ShowFileExtensions)
        {
          yield return CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ""HideFileExt"" /t REG_DWORD /d 0 /f");
        }

        switch (Configuration.HideFiles)
        {
          case HideModes.None:
            yield return CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ""Hidden"" /t REG_DWORD /d 1 /f");
            yield return CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ""ShowSuperHidden"" /t REG_DWORD /d 1 /f");
            break;
          case HideModes.HiddenSystem:
            yield return CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ""Hidden"" /t REG_DWORD /d 1 /f");
            break;
          case HideModes.Hidden:
            break;
        }
      }
      appender.Append(CommandBuilder.RegistryDefaultUserCommand(SetExplorerOptions));
    }

    if (Configuration.ShowAllTrayIcons)
    {
      string ps1File = @"C:\Windows\Setup\Scripts\ShowAllTrayIcons.ps1";
      string script = Util.StringFromResource("ShowAllTrayIcons.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }

    if (Configuration.DeleteTaskbarIcons)
    {
      string ps1File = @"C:\Windows\Setup\Scripts\DeleteTaskbarIcons.ps1";
      string script = Util.StringFromResource("DeleteTaskbarIcons.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }

    if (Configuration.DisableDefender)
    {
      CommandAppender pe = GetAppender(CommandConfig.WindowsPE);
      const string path = @"X:\disable-defender.vbs";
      foreach (string line in Util.SplitLines(Util.StringFromResource("disable-defender.vbs")))
      {
        pe.Append(
          CommandBuilder.WriteToFile(path, line)
        );
      }
      pe.Append(
        CommandBuilder.ShellCommand($"start /MIN cscript.exe //E:vbscript {path}")
      );
    }

    if (Configuration.DisableSac)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy"" /v VerifiedAndReputablePolicyState /t REG_DWORD /d 0 /f")
      );
    }

    if (Configuration.DisableSmartScreen)
    {
      appender.Append([
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"" /v SmartScreenEnabled /t REG_SZ /d ""Off"" /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WTDS\Components"" /v ServiceEnabled /t REG_DWORD /d 0 /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WTDS\Components"" /v NotifyMalicious /t REG_DWORD /d 0 /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WTDS\Components"" /v NotifyPasswordReuse /t REG_DWORD /d 0 /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WTDS\Components"" /v NotifyUnsafeApp /t REG_DWORD /d 0 /f"),
        ..CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          return [
            CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Edge\SmartScreenEnabled"" /ve /t REG_DWORD /d 0 /f"),
            CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Edge\SmartScreenPuaEnabled"" /ve /t REG_DWORD /d 0 /f"),
            CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\AppHost"" /v EnableWebContentEvaluation /t REG_DWORD /d 0 /f"),
            CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\AppHost"" /v PreventOverride /t REG_DWORD /d 0 /f"),
          ];
        })
      ]);
    }

    if (Configuration.DisableUac)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v EnableLUA /t REG_DWORD /d 0 /f")
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
        CommandBuilder.Raw(@"netsh.exe advfirewall firewall set rule group=""@FirewallAPI.dll,-28752"" new enable=Yes"),
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

    if (Configuration.DisableFastStartup)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power"" /v HiberbootEnabled /t REG_DWORD /d 0 /f")
      );
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
          string script = $"$mountKey = '{subKey}';\r\n" + Util.StringFromResource("TurnOffSystemSounds.ps1");
          string ps1File = @"%TEMP%\sounds.ps1";
          AddTextFile(script, ps1File);
          return [
            CommandBuilder.InvokePowerShellScript(ps1File),
            CommandBuilder.UserRunOnceCommand(rootKey, subKey, "NoSounds", CommandBuilder.RegistryCommand(@"add ""HKCU\AppEvents\Schemes"" /ve /t REG_SZ /d "".None"" /f")),
          ];
        }));
      appender.Append([
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation"" /v DisableStartupSound /t REG_DWORD /d 1 /f"),
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\EditionOverrides"" /v UserSetting_DisableStartupSound /t REG_DWORD /d 1 /f"),
      ]);
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

    if (Configuration.VMwareTools)
    {
      string ps1File = @"%TEMP%\VMwareTools.ps1";
      string script = Util.StringFromResource("VMwareTools.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }

    if (Configuration.VirtIoGuestTools)
    {
      string ps1File = @"%TEMP%\VirtIoGuestTools.ps1";
      string script = Util.StringFromResource("VirtIoGuestTools.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }

    if (Configuration.PreventDeviceEncryption)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SYSTEM\CurrentControlSet\Control\BitLocker"" /v ""PreventDeviceEncryption"" /t REG_DWORD /d 1 /f")
      );
    }

    if (Configuration.ClassicContextMenu)
    {
      appender.Append(
        CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          return [CommandBuilder.UserRunOnceCommand(rootKey, subKey, "ClassicContextMenu", CommandBuilder.RegistryCommand(@$"add ""HKCU\Software\Classes\CLSID\{{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}}\InprocServer32"" /ve /f"))];
        })
      );
    }

    if (Configuration.LeftTaskbar)
    {
      appender.Append(
        CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          return [CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarAl /t REG_DWORD /d 0 /f")];
        })
      );
    }

    if (Configuration.HideEdgeFre)
    {
      appender.Append(
        CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Policies\Microsoft\Edge"" /v HideFirstRunExperience /t REG_DWORD /d 1 /f")
      );
    }
  }
}
