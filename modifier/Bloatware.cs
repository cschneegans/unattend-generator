using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    string ps1File = parent.EmbedTextFile($"{BaseName}.ps1", GetRemoveCommand());
    parent.SpecializeScript.InvokeFile(ps1File);
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
    sw.WriteLine($@"$logfile = 'C:\Windows\Setup\Scripts\{BaseName}.log';");
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
  protected override string GetCommand => """
    {
      Get-AppxProvisionedPackage -Online;
    }
    """;

  protected override string FilterCommand => """
    {
      $_.DisplayName -eq $selector;
    }
    """;

  protected override string RemoveCommand => """
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

  protected override string BaseName => "RemovePackages";

  protected override string Type => "Package";
}

class CapabilityRemover : Remover<CapabilityBloatwareStep>
{
  protected override string GetCommand => """
    {
      Get-WindowsCapability -Online | Where-Object -Property 'State' -NotIn -Value @(
        'NotPresent';
        'Removed';
      );
    }
    """;

  protected override string FilterCommand => """
    {
      ($_.Name -split '~')[0] -eq $selector;
    }
    """;

  protected override string RemoveCommand => """
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

  protected override string BaseName => "RemoveCapabilities";

  protected override string Type => "Capability";
}

class FeatureRemover : Remover<OptionalFeatureBloatwareStep>
{
  protected override string GetCommand => """
    {
      Get-WindowsOptionalFeature -Online | Where-Object -Property 'State' -NotIn -Value @(
        'Disabled';
        'DisabledWithPayloadRemoved';
      );
    }
    """;

  protected override string FilterCommand => """
    {
      $_.FeatureName -eq $selector;
    }
    """;

  protected override string RemoveCommand => """
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

  protected override string BaseName => "RemoveFeatures";

  protected override string Type => "Feature";
}

class BloatwareModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
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
            SpecializeScript.Append(@"Remove-Item -LiteralPath 'C:\Users\Default\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\OneDrive.lnk', 'C:\Windows\System32\OneDriveSetup.exe', 'C:\Windows\SysWOW64\OneDriveSetup.exe' -ErrorAction 'Continue';");
            DefaultUserScript.Append(@"Remove-ItemProperty -LiteralPath 'Registry::HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDriveSetup' -Force -ErrorAction 'Continue';");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveTeams":
            SpecializeScript.Append(@"reg.exe add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Communications"" /v ConfigureChatAutoInstall /t REG_DWORD /d 0 /f;");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveNotepad":
            SpecializeScript.Append("""
              reg.exe add "HKCR\.txt\ShellNew" /v ItemName /t REG_EXPAND_SZ /d "@C:\Windows\system32\notepad.exe,-470" /f;
              reg.exe add "HKCR\.txt\ShellNew" /v NullFile /t REG_SZ /f;
              reg.exe add "HKCR\txtfilelegacy" /v FriendlyTypeName /t REG_EXPAND_SZ /d "@C:\Windows\system32\notepad.exe,-469" /f;
              reg.exe add "HKCR\txtfilelegacy" /ve /t REG_SZ /d "Text Document" /f;
              """);
            DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Microsoft\Notepad"" /v ShowStoreBanner /t REG_DWORD /d 0 /f;");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveOutlook":
            SpecializeScript.Append(@"Remove-Item -LiteralPath 'Registry::HKLM\Software\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\OutlookUpdate' -Force -ErrorAction 'SilentlyContinue';");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveDevHome":
            SpecializeScript.Append(@"Remove-Item -LiteralPath 'Registry::HKLM\Software\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\DevHomeUpdate' -Force -ErrorAction 'SilentlyContinue';");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveCopilot":
            UserOnceScript.Append("Get-AppxPackage -Name 'Microsoft.Windows.Ai.Copilot.Provider' | Remove-AppxPackage;");
            DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Policies\Microsoft\Windows\WindowsCopilot"" /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f;");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveXboxApps":
            DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\GameDVR"" /v AppCaptureEnabled /t REG_DWORD /d 0 /f;");
            break;
          case CustomBloatwareStep when bw.Id == "RemoveInternetExplorer":
            DefaultUserScript.Append(@$"reg.exe add ""HKU\DefaultUser\Software\Microsoft\Internet Explorer\LowRegistry\Audio\PolicyConfig\PropertyStore"" /f;");
            break;
          default:
            throw new NotSupportedException();
        }
      }
    }

    packageRemover.Save(this);
    capabilityRemover.Save(this);
    featureRemover.Save(this);
  }
}
