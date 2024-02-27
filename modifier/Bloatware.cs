using System;
using System.Xml;

namespace Schneegans.Unattend;

class ContentInfo(string baseName)
{
  public string BaseName { get; } = baseName ?? throw new ArgumentNullException(nameof(baseName));

  public string CmdPath => @$"%TEMP%\{BaseName}.txt";

  public string PsPath => @$"$env:TEMP\{BaseName}.txt";

  public string PsLogPath => @$"$env:TEMP\{BaseName}.log";

  public bool HasContent { get; set; } = false;
}

class BloatwareModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Specialize);

    var packages = new ContentInfo("remove-packages");
    var caps = new ContentInfo("remove-caps");

    void RemovePackage(PackageBloatwareStep step)
    {
      appender.WriteToFile(packages.CmdPath, step.Selector);
      packages.HasContent = true;
    }

    void RemoveCapability(CapabilityBloatwareStep step)
    {
      appender.WriteToFile(caps.CmdPath, step.Selector);
      caps.HasContent = true;
    }

    foreach (Bloatware bw in Configuration.Bloatwares)
    {
      foreach (BloatwareStep step in bw.Steps)
      {
        switch (step)
        {
          case PackageBloatwareStep package:
            {
              RemovePackage(package);
              break;
            }

          case CapabilityBloatwareStep capability:
            {
              RemoveCapability(capability);
              break;
            }
          case CustomBloatwareStep when bw.Key == "RemoveOneDrive":
            {
              appender.ShellCommand(@"del ""C:\Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\OneDrive.lnk""");
              appender.ShellCommand(@"del ""C:\Windows\System32\OneDriveSetup.exe""");
              appender.ShellCommand(@"del ""C:\Windows\SysWOW64\OneDriveSetup.exe""");
              appender.RegistryDefaultUserCommand((rootKey, subKey) =>
              {
                appender.RegistryCommand(@$"delete ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\Run"" /v OneDriveSetup /f");
              });
              break;
            }
          case CustomBloatwareStep when bw.Key == "RemoveTeams":
            {
              appender.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Communications"" /v ConfigureChatAutoInstall /t REG_DWORD /d 0 /f");
              break;
            }
          case CustomBloatwareStep when bw.Key == "RemoveNotepad":
            {
              appender.RegistryDefaultUserCommand((rootKey, subKey) =>
              {
                appender.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Notepad"" /v ShowStoreBanner /t REG_DWORD /d 0 /f");
              });
              break;
            }
          case CustomBloatwareStep when bw.Key == "RemoveOutlook":
            {
              appender.RegistryCommand(@"delete ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\OutlookUpdate"" /f");
              break;
            }
          case CustomBloatwareStep when bw.Key == "RemoveDevHome":
            {
              appender.RegistryCommand(@"delete ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\DevHomeUpdate"" /f");
              break;
            }
          default:
            throw new NotSupportedException();
        }
      }
    }

    if (packages.HasContent)
    {
      appender.PowerShellCommand(@$"Get-AppxProvisionedPackage -Online | where DisplayName -In (Get-Content {packages.PsPath} ) | Remove-AppxProvisionedPackage -AllUsers -Online *>&1 >> {packages.PsLogPath};");
    }

    if (caps.HasContent)
    {
      appender.PowerShellCommand(@$"Get-WindowsCapability -Online | where {{($_.Name -split '~')[0] -in (Get-Content {caps.PsPath} ) }} | Remove-WindowsCapability -Online *>&1 >> {caps.PsLogPath};");
    }

    if (!Configuration.Bloatwares.IsEmpty)
    {
      {
        // Windows 10
        string xml = "<LayoutModificationTemplate Version='1' xmlns='http://schemas.microsoft.com/Start/2014/LayoutModification'>" +
          "<LayoutOptions StartTileGroupCellWidth='6' />" +
          "<DefaultLayoutOverride>" +
          "<StartLayoutCollection>" +
          "<StartLayout GroupCellWidth='6' xmlns='http://schemas.microsoft.com/Start/2014/FullDefaultLayout' />" +
          "</StartLayoutCollection>" +
          "</DefaultLayoutOverride>" +
          "</LayoutModificationTemplate>";
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        appender.WriteToFile(@"C:\Users\Default\AppData\Local\Microsoft\Windows\Shell\LayoutModification.xml", doc);
      }
      {
        // Windows 11
        string json = @"""{ \""pinnedList\"": [] }""";
        string guid = "B5292708-1619-419B-9923-E5D9F3925E71";
        {
          string key = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start";
          appender.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins /t REG_SZ /d {json} /f");
          appender.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins_ProviderSet /t REG_DWORD /d 1 /f");
          appender.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins_WinningProvider /t REG_SZ /d {guid} /f");
        }
        {
          string key = $@"HKLM\SOFTWARE\Microsoft\PolicyManager\providers\{guid}\default\Device\Start";
          appender.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins /t REG_SZ /d {json} /f");
          appender.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins_LastWrite /t REG_DWORD /d 1 /f");
        }
      }
    }
  }
}
