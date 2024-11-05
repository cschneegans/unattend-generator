using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

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

public interface IKeySettings;

public class SkipKeySettings : IKeySettings;

public enum KeyInitial
{
  Off, On
}

public enum KeyBehavior
{
  Toggle, Ignore
}

public record class KeySetting(
  KeyInitial Initial,
  KeyBehavior Behavior
);

public record class ConfigureKeySettings(
  KeySetting CapsLock,
  KeySetting NumLock,
  KeySetting ScrollLock
) : IKeySettings;

public interface IStartPinsSettings;

public class DefaultStartPinsSettings : IStartPinsSettings;

public class EmptyStartPinsSettings : IStartPinsSettings;

public record class CustomStartPinsSettings(
  string Json
) : IStartPinsSettings;

public interface IStartTilesSettings;

public class DefaultStartTilesSettings : IStartTilesSettings;

public class EmptyStartTilesSettings : IStartTilesSettings;

public record class CustomStartTilesSettings(
  string Xml
) : IStartTilesSettings;

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

    if (Configuration.DisableWindowsUpdate)
    {
      AddTextFile(
        Util.StringFromResource("PauseWindowsUpdate.ps1"),
        @"C:\Windows\Setup\Scripts\PauseWindowsUpdate.ps1"
      );
      AddXmlFile(
        Util.XmlDocumentFromResource("PauseWindowsUpdate.xml"),
        @"C:\Windows\Setup\Scripts\PauseWindowsUpdate.xml"
      );
      appender.Append(
        CommandBuilder.PowerShellCommand(@"Register-ScheduledTask -TaskName 'PauseWindowsUpdate' -Xml $( Get-Content -LiteralPath 'C:\Windows\Setup\Scripts\PauseWindowsUpdate.xml' -Raw );")
      );
    }

    if (Configuration.ShowAllTrayIcons)
    {
      string ps1File = @"C:\Windows\Setup\Scripts\ShowAllTrayIcons.ps1";
      string script = Util.StringFromResource("ShowAllTrayIcons.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
      AddXmlFile(Util.XmlDocumentFromResource("ShowAllTrayIcons.xml"), @"C:\Windows\Setup\Scripts\ShowAllTrayIcons.xml");
      AddTextFile(Util.StringFromResource("ShowAllTrayIcons.vbs"), @"C:\Windows\Setup\Scripts\ShowAllTrayIcons.vbs");
    }

    if (Configuration.DeleteTaskbarIcons)
    {
      string ps1File = @"C:\Windows\Setup\Scripts\TaskbarIcons.ps1";
      string script = Util.StringFromResource("TaskbarIcons.ps1");
      AddTextFile(script, ps1File);
      UserOnceScript.InvokeFile(ps1File);
      UserOnceScript.RestartExplorer();
    }

    if (Configuration.HideTaskViewButton)
    {
      appender.Append(
        CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          return [
            CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ShowTaskViewButton /t REG_DWORD /d 0 /f"),
          ];
        })
      );
    }

    if (Configuration.DisableDefender)
    {
      CommandAppender pe = GetAppender(CommandConfig.WindowsPE);
      const string path = @"X:\defender.vbs";
      pe.Append([
        ..CommandBuilder.WriteToFile(path, Util.SplitLines(Util.StringFromResource("DisableDefender.vbs"))),
        CommandBuilder.ShellCommand($"start /MIN cscript.exe //E:vbscript {path}")
      ]);
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
          StringWriter writer = new();
          writer.WriteLine(@$"$mountKey = '{rootKey}\{subKey}';");
          writer.WriteLine(Util.StringFromResource("TurnOffSystemSounds.ps1"));
          string ps1File = @"%TEMP%\TurnOffSystemSounds.ps1";
          AddTextFile(writer.ToString(), ps1File);
          return [
            CommandBuilder.InvokePowerShellScript(ps1File),
          ];
        }));
      UserOnceScript.Append(@"Set-ItemProperty -LiteralPath 'Registry::HKCU\AppEvents\Schemes' -Name '(Default)' -Type 'String' -Value '.None';");

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
      string ps1File = @"C:\Windows\Setup\Scripts\VMwareTools.ps1";
      string script = Util.StringFromResource("VMwareTools.ps1");
      AddTextFile(script, ps1File);
      (Configuration.DisableDefender ? GetAppender(CommandConfig.Specialize) : GetAppender(CommandConfig.Oobe)).Append(
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
      UserOnceScript.Append(Util.StringFromResource("ClassicContextMenu.ps1"));
      UserOnceScript.RestartExplorer();
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
    {
      if (Configuration.KeySettings is ConfigureKeySettings settings)
      {
        {
          uint indicators = 0;

          if (settings.CapsLock.Initial == KeyInitial.On)
          {
            indicators |= 1;
          }
          if (settings.NumLock.Initial == KeyInitial.On)
          {
            indicators |= 2;
          }
          if (settings.ScrollLock.Initial == KeyInitial.On)
          {
            indicators |= 4;
          }
          appender.Append(
            CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
              {
                return [
                  CommandBuilder.RegistryCommand(@$"add ""HKU\.DEFAULT\Control Panel\Keyboard"" /v InitialKeyboardIndicators /t REG_SZ /d ""{indicators}"" /f"),
                  CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Control Panel\Keyboard"" /v InitialKeyboardIndicators /t REG_SZ /d ""{indicators}"" /f")
                ];
              }
            )
          );
        }
        {
          bool ignoreCapsLock = settings.CapsLock.Behavior == KeyBehavior.Ignore;
          bool ignoreNumLock = settings.NumLock.Behavior == KeyBehavior.Ignore;
          bool ignoreScrollLock = settings.ScrollLock.Behavior == KeyBehavior.Ignore;

          uint count = 0;
          if (ignoreCapsLock)
          {
            count++;
          }
          if (ignoreNumLock)
          {
            count++;
          }
          if (ignoreScrollLock)
          {
            count++;
          }
          if (count > 0)
          {
            MemoryStream mstr = new();
            mstr.Write(new byte[4]); // Version
            mstr.Write(new byte[4]); // Flags
            mstr.Write(BitConverter.GetBytes(count + 1)); // Count
            if (ignoreCapsLock)
            {
              mstr.Write([0, 0, 0x3A, 0]);
            }
            if (ignoreNumLock)
            {
              mstr.Write([0, 0, 0x45, 0]);
            }
            if (ignoreScrollLock)
            {
              mstr.Write([0, 0, 0x46, 0]);
            }
            mstr.Write(new byte[4]); // Footer
            string base64 = Convert.ToBase64String(mstr.ToArray());

            appender.Append(
              CommandBuilder.PowerShellCommand(@$"Set-ItemProperty -LiteralPath 'Registry::HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout' -Name 'Scancode Map' -Type 'Binary' -Value([convert]::FromBase64String('{base64}'));")
            );
          }
        }
      }
    }
    if (Configuration.MakeEdgeUninstallable)
    {
      string ps1File = @"C:\Windows\Setup\Scripts\MakeEdgeUninstallable.ps1";
      string script = Util.StringFromResource("MakeEdgeUninstallable.ps1");
      AddTextFile(script, ps1File);
      appender.Append(
        CommandBuilder.InvokePowerShellScript(ps1File)
      );
    }
    {
      if (Configuration.LaunchToThisPC)
      {
        UserOnceScript.Append(@"Set-ItemProperty -LiteralPath 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'LaunchTo' -Type 'DWord' -Value 1;");
      }
    }
    {
      UserOnceScript.Append(@$"Set-ItemProperty -LiteralPath 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Search' -Name 'SearchboxTaskbarMode' -Type 'DWord' -Value {Configuration.TaskbarSearch:D};");
    }
    {
      void SetStartPins(string json)
      {
        string ps1File = @"C:\Windows\Setup\Scripts\SetStartPins.ps1";
        string script = Util.StringFromResource("SetStartPins.ps1");
        StringWriter writer = new();
        writer.WriteLine($"$json = '{json.Replace("'", "''")}';");
        writer.WriteLine(script);
        AddTextFile(writer.ToString(), ps1File);
        appender.Append(
          CommandBuilder.InvokePowerShellScript(ps1File)
        );
      }

      switch (Configuration.StartPinsSettings)
      {
        case DefaultStartPinsSettings:
          break;

        case EmptyStartPinsSettings:
          SetStartPins(@"{""pinnedList"":[]}");
          break;

        case CustomStartPinsSettings settings:
          try
          {
            using JsonTextReader reader = new(new StringReader(settings.Json));
            while (reader.Read()) { }
          }
          catch
          {
            throw new ConfigurationException($"The string '{settings.Json}' is not valid JSON.");
          }
          SetStartPins(settings.Json.Trim());
          break;

        default:
          throw new NotSupportedException();
      }
    }
    {
      void SetStartTiles(string xml)
      {
        XmlDocument doc = new();
        doc.LoadXml(xml);
        AddXmlFile(doc, @"C:\Users\Default\AppData\Local\Microsoft\Windows\Shell\LayoutModification.xml");
      }

      switch (Configuration.StartTilesSettings)
      {
        case DefaultStartTilesSettings:
          break;

        case EmptyStartTilesSettings:
          string xml = """
          <LayoutModificationTemplate Version='1' xmlns='http://schemas.microsoft.com/Start/2014/LayoutModification'>
            <LayoutOptions StartTileGroupCellWidth='6' />
            <DefaultLayoutOverride>
              <StartLayoutCollection>
                <StartLayout GroupCellWidth='6' xmlns='http://schemas.microsoft.com/Start/2014/FullDefaultLayout' />
              </StartLayoutCollection>
            </DefaultLayoutOverride>
          </LayoutModificationTemplate>
          """;
          SetStartTiles(xml);
          break;

        case CustomStartTilesSettings settings:
          SetStartTiles(settings.Xml);
          break;

        default:
          throw new NotSupportedException();
      }
    }
  }
}
