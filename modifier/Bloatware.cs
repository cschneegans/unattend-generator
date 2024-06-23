using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

abstract class Remover<T> where T : SelectorBloatwareStep
{
  private readonly List<string> selectors = [];

  public void Add(T step)
  {
    selectors.Add(step.Selector);
  }

  public void Save(BloatwareModifier parent)
  {
    if (selectors.Count == 0)
    {
      return;
    }
    string scriptPath = @$"C:\Windows\Temp\{BaseName}.ps1";
    parent.AddTextFile(GetRemoveCommand(), scriptPath);
    CommandAppender appender = parent.GetAppender(CommandConfig.Specialize);
    appender.Append(
      CommandBuilder.InvokePowerShellScript(scriptPath)
    );
  }

  private string GetRemoveCommand()
  {
    StringWriter sw = new();
    sw.WriteLine("$selectors = @(");
    foreach (string selector in selectors)
    {
      sw.WriteLine($"\t'{selector}';");
    }
    sw.WriteLine(");");
    sw.WriteLine($"$getCommand = {GetCommand};");
    sw.WriteLine($"$filterCommand = {FilterCommand};");
    sw.WriteLine($"$removeCommand = {RemoveCommand};");
    sw.WriteLine($"$type = '{Type}';");
    sw.WriteLine($@"$logfile = 'C:\Windows\Temp\{BaseName}.log';");
    return sw.ToString() + Util.StringFromResource("RemoveBloatware.ps1");
  }

  protected abstract string GetCommand { get; }

  protected abstract string FilterCommand { get; }

  protected abstract string RemoveCommand { get; }

  protected abstract string BaseName { get; }

  protected abstract string Type { get; }
}

class PackageRemover : Remover<PackageBloatwareStep>
{
  protected override string GetCommand => "{ Get-AppxProvisionedPackage -Online; }";

  protected override string FilterCommand => "{ $_.DisplayName -eq $selector; }";

  protected override string RemoveCommand =>
  """
  {
    [CmdletBinding()]
    param(
      [Parameter( Mandatory, ValueFromPipeline )]
      $InputObject
    );
    process {
      $InputObject | Remove-AppxProvisionedPackage -AllUsers -Online -ErrorAction 'Continue';
    }
  }
  """;

  protected override string BaseName => "remove-packages";

  protected override string Type => "Package";
}

class CapabilityRemover : Remover<CapabilityBloatwareStep>
{
  protected override string GetCommand => "{ Get-WindowsCapability -Online; }";

  protected override string FilterCommand => "{ ($_.Name -split '~')[0] -eq $selector; }";

  protected override string RemoveCommand =>
  """
  {
    [CmdletBinding()]
    param(
      [Parameter( Mandatory, ValueFromPipeline )]
      $InputObject
    );
    process {
      $InputObject | Remove-WindowsCapability -Online -ErrorAction 'Continue';
    }
  }
  """;

  protected override string BaseName => "remove-caps";

  protected override string Type => "Capability";
}

class FeatureRemover : Remover<OptionalFeatureBloatwareStep>
{
  protected override string GetCommand => "{ Get-WindowsOptionalFeature -Online; }";

  protected override string FilterCommand => "{ $_.FeatureName -eq $selector; }";

  protected override string RemoveCommand =>
  """
  {
    [CmdletBinding()]
    param(
      [Parameter( Mandatory, ValueFromPipeline )]
      $InputObject
    );
    process {
      $InputObject | Disable-WindowsOptionalFeature -Online -Remove -NoRestart -ErrorAction 'Continue';
    }
  }
  """;

  protected override string BaseName => "remove-features";

  protected override string Type => "Feature";
}

class BloatwareModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    CommandAppender appender = GetAppender(CommandConfig.Specialize);

    var packageRemover = new PackageRemover();
    var capabilityRemover = new CapabilityRemover();
    var featureRemover = new FeatureRemover();

    foreach (Bloatware bw in Configuration.Bloatwares.OrderBy(bw => bw.Id))
    {
      foreach (BloatwareStep step in bw.Steps)
      {
        switch (step)
        {
          case PackageBloatwareStep package:
            packageRemover.Add(package);
            break;
          case CapabilityBloatwareStep capability:
            capabilityRemover.Add(capability);
            break;
          case OptionalFeatureBloatwareStep feature:
            featureRemover.Add(feature);
            break;
          case CustomBloatwareStep when bw.Id == "RemoveOneDrive":
            appender.Append([
              CommandBuilder.ShellCommand(@"del ""C:\Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\OneDrive.lnk"""),
              CommandBuilder.ShellCommand(@"del ""C:\Windows\System32\OneDriveSetup.exe"""),
              CommandBuilder.ShellCommand(@"del ""C:\Windows\SysWOW64\OneDriveSetup.exe"""),
            ]);
            appender.Append(
              CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
              {
                return [CommandBuilder.RegistryCommand(@$"delete ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\Run"" /v OneDriveSetup /f")];
              })
            );
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
          case CustomBloatwareStep when bw.Id == "RemoveCopilot":
            appender.Append(
              CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
              {
                return [
                  CommandBuilder.UserRunOnceCommand("UninstallCopilot", CommandBuilder.PowerShellCommand("Get-AppxPackage -Name 'Microsoft.Windows.Ai.Copilot.Provider' | Remove-AppxPackage;"), rootKey, subKey),
                  CommandBuilder.RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Policies\Microsoft\Windows\WindowsCopilot"" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f")
                ];
              })
            );
            break;
          default:
            throw new NotSupportedException();
        }
      }
    }

    packageRemover.Save(this);
    capabilityRemover.Save(this);
    featureRemover.Save(this);

    if (!Configuration.Bloatwares.IsEmpty)
    {
      {
        // Windows 10
        XmlDocument xml = new();
        xml.LoadXml("""
          <LayoutModificationTemplate Version='1' xmlns='http://schemas.microsoft.com/Start/2014/LayoutModification'>
            <LayoutOptions StartTileGroupCellWidth='6' />
            <DefaultLayoutOverride>
              <StartLayoutCollection>
                <StartLayout GroupCellWidth='6' xmlns='http://schemas.microsoft.com/Start/2014/FullDefaultLayout' />
              </StartLayoutCollection>
            </DefaultLayoutOverride>
          </LayoutModificationTemplate>
          """);
        AddXmlFile(xml, @"C:\Users\Default\AppData\Local\Microsoft\Windows\Shell\LayoutModification.xml");
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
