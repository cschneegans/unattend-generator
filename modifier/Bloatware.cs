using System;
using System.Xml;

namespace Schneegans.Unattend;

abstract class Remover<T> where T : SelectorBloatwareStep
{
  private bool hasContent = false;

  protected string CmdPath => @$"%TEMP%\{Tag()}.txt";

  protected string PsPath => @$"$env:TEMP\{Tag()}.txt";

  protected string PsLogPath => @$"$env:TEMP\{Tag()}.log";

  public void Add(T step, CommandAppender appender)
  {
    appender.Append(
      CommandBuilder.WriteToFile(CmdPath, step.Selector)
    );
    hasContent = true;
  }

  public void Write(CommandAppender appender)
  {
    if (hasContent)
    {
      appender.Append(RemoveCommand());
    }
  }

  protected abstract string RemoveCommand();

  protected abstract string Tag();
}

class PackageRemover : Remover<PackageBloatwareStep>
{
  protected override string RemoveCommand()
  {
    return CommandBuilder.PowerShellCommand(@$"Get-AppxProvisionedPackage -Online | where DisplayName -In (Get-Content {PsPath} ) | Remove-AppxProvisionedPackage -AllUsers -Online *>&1 >> {PsLogPath};");
  }

  protected override string Tag()
  {
    return "remove-packages";
  }
}

class CapabilityRemover : Remover<CapabilityBloatwareStep>
{
  protected override string RemoveCommand()
  {
    return CommandBuilder.PowerShellCommand(@$"Get-WindowsCapability -Online | where {{($_.Name -split '~')[0] -in (Get-Content {PsPath} ) }} | Remove-WindowsCapability -Online *>&1 >> {PsLogPath};");
  }

  protected override string Tag()
  {
    return "remove-caps";
  }
}

class FeatureRemover : Remover<OptionalFeatureBloatwareStep>
{
  protected override string RemoveCommand()
  {
    return CommandBuilder.PowerShellCommand(@$"Get-WindowsOptionalFeature -Online | where FeatureName -In (Get-Content {PsPath} ) | Disable-WindowsOptionalFeature -Online -Remove -NoRestart *>&1 >> {PsLogPath};");
  }

  protected override string Tag()
  {
    return "remove-features";
  }
}

class BloatwareModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Specialize);

    var packageRemover = new PackageRemover();
    var capabilityRemover = new CapabilityRemover();
    var featureRemover = new FeatureRemover();

    foreach (Bloatware bw in Configuration.Bloatwares)
    {
      foreach (BloatwareStep step in bw.Steps)
      {
        switch (step)
        {
          case PackageBloatwareStep package:
            packageRemover.Add(package, appender);
            break;
          case CapabilityBloatwareStep capability:
            capabilityRemover.Add(capability, appender);
            break;
          case OptionalFeatureBloatwareStep feature:
            featureRemover.Add(feature, appender);
            break;
          case CustomBloatwareStep when bw.Id == "RemoveOneDrive":
            appender.Append([
              CommandBuilder.ShellCommand(@"del ""C:\Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\OneDrive.lnk"""),
              CommandBuilder.ShellCommand(@"del ""C:\Windows\System32\OneDriveSetup.exe"""),
              CommandBuilder.ShellCommand(@"del ""C:\Windows\SysWOW64\OneDriveSetup.exe"""),
              .. CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) => {
                  return [CommandBuilder.RegistryCommand(@$"delete ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\Run"" /v OneDriveSetup /f")];
              }),
            ]);
            break;
          case CustomBloatwareStep when bw.Id == "RemoveTeams":
            appender.Append(
              CommandBuilder.RegistryCommand(@"add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Communications"" /v ConfigureChatAutoInstall /t REG_DWORD /d 0 /f")
            );
            break;
          case CustomBloatwareStep when bw.Id == "RemoveNotepad":
            appender.Append(
              CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
              {
                return [CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Notepad"" /v ShowStoreBanner /t REG_DWORD /d 0 /f")];
              })
            );
            break;
          case CustomBloatwareStep when bw.Id == "RemoveOutlook":
            appender.Append(
              CommandBuilder.RegistryCommand(@"delete ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\OutlookUpdate"" /f")
            );
            break;
          case CustomBloatwareStep when bw.Id == "RemoveDevHome":
            appender.Append(
              CommandBuilder.RegistryCommand(@"delete ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\DevHomeUpdate"" /f")
            );
            break;
          default:
            throw new NotSupportedException();
        }
      }
    }

    packageRemover.Write(appender);
    capabilityRemover.Write(appender);
    featureRemover.Write(appender);

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
        appender.Append(
          CommandBuilder.WriteToFile(@"C:\Users\Default\AppData\Local\Microsoft\Windows\Shell\LayoutModification.xml", doc)
        );
      }
      {
        // Windows 11
        string json = @"""{ \""pinnedList\"": [] }""";
        string guid = "B5292708-1619-419B-9923-E5D9F3925E71";
        {
          string key = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start";
          appender.Append([
            CommandBuilder.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins /t REG_SZ /d {json} /f"),
            CommandBuilder.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins_ProviderSet /t REG_DWORD /d 1 /f"),
            CommandBuilder.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins_WinningProvider /t REG_SZ /d {guid} /f"),
          ]);
        }
        {
          string key = $@"HKLM\SOFTWARE\Microsoft\PolicyManager\providers\{guid}\default\Device\Start";
          appender.Append([
            CommandBuilder.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins /t REG_SZ /d {json} /f"),
            CommandBuilder.RegistryCommand($@"add ""{key}"" /v ConfigureStartPins_LastWrite /t REG_DWORD /d 1 /f"),
          ]);
        }
      }
    }
  }
}
