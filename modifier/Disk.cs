using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

public interface IPartitionSettings;

public record class CustomPartitionSettings(
  string Script
) : IPartitionSettings;

public record class UnattendedPartitionSettings(
  int TargetDisk,
  PartitionLayout PartitionLayout,
  RecoveryMode RecoveryMode,
  int EspSize = Constants.EspDefaultSize,
  int RecoverySize = Constants.RecoveryPartitionSize
) : IPartitionSettings;

public interface IDiskAssertionSettings;

public class SkipDiskAssertionSettings : IDiskAssertionSettings;

public record class GeneratedDiskAssertionsSettings(
  int? MinSizeGiB,
  int? MaxSizeGiB,
  bool AssertNoPartitions,
  bool AssertInterfaceType,
  bool AssertMediaType
) : IDiskAssertionSettings;

public record class NoPartitionsDiskAssertionsSettings : GeneratedDiskAssertionsSettings
{
  public NoPartitionsDiskAssertionsSettings() : base(
    MinSizeGiB: null,
    MaxSizeGiB: null,
    AssertNoPartitions: true,
    AssertInterfaceType: false,
    AssertMediaType: false
  )
  { }
}

public record class HardDiskAssertionsSettings : GeneratedDiskAssertionsSettings
{
  public const int DefaultMinSizeGiB = 100;

  public HardDiskAssertionsSettings() : base(
    MinSizeGiB: DefaultMinSizeGiB,
    MaxSizeGiB: null,
    AssertNoPartitions: false,
    AssertInterfaceType: true,
    AssertMediaType: true
  )
  { }
}

public record class ScriptDiskAssertionsSettings(
  string Script
) : IDiskAssertionSettings;

public interface IInstallFromSettings;

public class AutomaticInstallFromSettings : IInstallFromSettings;

public record class IndexInstallFromSettings(
  int Index
) : IInstallFromSettings;

public record class NameInstallFromSettings(
  string Name
) : IInstallFromSettings;

public interface IPESettings;

public record class DefaultPESettings(
  bool BypassRequirementsCheck
) : IPESettings;

public interface ICmdPESettings : IPESettings;

public record class GeneratePESettings(
  IPartitionSettings PartitionSettings,
  IDiskAssertionSettings DiskAssertionSettings,
  IInstallFromSettings InstallFromSettings,
  bool DisableDefender,
  bool Disable8Dot3Names,
  bool PauseBeforeFormatting,
  bool PauseBeforeReboot,
  bool CompactOs
) : ICmdPESettings;

public record class ScriptPESetttings(
  string Script
) : ICmdPESettings;

static class Paths
{
  static internal readonly string PEScript = @"X:\pe.cmd";
  static internal readonly string DiskpartScript = @"X:\diskpart.txt";
  static internal readonly string DiskpartLog = @"X:\diskpart.log";
  static internal readonly string AssertScript = @"X:\assert.vbs";
}

class DiskModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.PESettings is ICmdPESettings)
    {
      {
        string comp = "Microsoft-Windows-PnpCustomizationsWinPE";
        if (Configuration.Components.Any(c => c.Key.Component == comp))
        {
          throw new ConfigurationException($"Cannot create .cmd script when component ‘{comp}’ is used. Consider using a custom script and the ‘drvload.exe’ command.");
        }
      }

      if (Configuration.Components.Any(c => c.Key.Pass == Pass.windowsPE))
      {
        throw new ConfigurationException("Cannot create .cmd script when custom component with pass ‘windowsPE’ is used.");
      }

      foreach (var node in Document.SelectNodesOrEmpty($"/u:unattend/u:settings[@pass='{Pass.windowsPE}']/*", NamespaceManager))
      {
        node.RemoveSelf();
      }

      if (Configuration.UseNarrator)
      {
        GetAppender(CommandConfig.WindowsPE).Append(
          CommandBuilder.ShellCommand(@"start X:\Windows\System32\Narrator.exe")
        );
      }
    }

    switch (Configuration.PESettings)
    {
      case ScriptPESetttings peSettings:
        WritePeScript(Util.SplitLines(peSettings.Script));
        break;

      case GeneratePESettings peSettings:
        WritePeScript(GetPEScript(Configuration, peSettings, Generator));
        break;

      case DefaultPESettings:
        break;

      default:
        throw new NotSupportedException();
    }
  }

  /// <summary>
  /// Creates the .cmd script that handles the PE stage of Windows Setup instead of setup.exe.
  /// </summary>
  private void WritePeScript(IEnumerable<string> lines)
  {
    CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
    appender.Append([
      ..CommandBuilder.WriteToFilePE(Paths.PEScript, lines),
      CommandBuilder.ShellCommand(Paths.PEScript)
    ]);
  }

  internal static List<string> GetDiskpartScript(UnattendedPartitionSettings settings, char bootDrive = 'S', char windowsDrive = 'W', char recoveryDrive = 'R')
  {
    string IfRecovery(string line)
    {
      return settings.RecoveryMode == RecoveryMode.Partition ? line : "";
    }

    return settings.PartitionLayout switch
    {
      PartitionLayout.MBR =>
      [
        $"SELECT DISK={settings.TargetDisk}",
        "CLEAN",
        "CREATE PARTITION PRIMARY SIZE=100",
        @"FORMAT QUICK FS=NTFS LABEL=""System Reserved""",
        $"ASSIGN LETTER={bootDrive}",
        "ACTIVE",
        "CREATE PARTITION PRIMARY",
        (IfRecovery($"SHRINK MINIMUM={settings.RecoverySize}")),
        @"FORMAT QUICK FS=NTFS LABEL=""Windows""",
        $"ASSIGN LETTER={windowsDrive}",
        (IfRecovery("CREATE PARTITION PRIMARY")),
        (IfRecovery(@"FORMAT QUICK FS=NTFS LABEL=""Recovery""")),
        (IfRecovery($"ASSIGN LETTER={recoveryDrive}")),
        (IfRecovery("SET ID=27"))
      ],
      PartitionLayout.GPT =>
      [
        $"SELECT DISK={settings.TargetDisk}",
        "CLEAN",
        "CONVERT GPT",
        $"CREATE PARTITION EFI SIZE={settings.EspSize}",
        @"FORMAT QUICK FS=FAT32 LABEL=""System""",
        $"ASSIGN LETTER={bootDrive}",
        "CREATE PARTITION MSR SIZE=16",
        "CREATE PARTITION PRIMARY",
        (IfRecovery($"SHRINK MINIMUM={settings.RecoverySize}")),
        @"FORMAT QUICK FS=NTFS LABEL=""Windows""",
        $"ASSIGN LETTER={windowsDrive}",
        (IfRecovery("CREATE PARTITION PRIMARY")),
        (IfRecovery(@"FORMAT QUICK FS=NTFS LABEL=""Recovery""")),
        (IfRecovery($"ASSIGN LETTER={recoveryDrive}")),
        (IfRecovery(@"SET ID=""de94bba4-06d1-4d40-a16a-bfd50179d6ac""")),
        (IfRecovery("GPT ATTRIBUTES=0x8000000000000001"))
      ],
      _ => throw new NotSupportedException()
    };
  }

  private static List<string> GetDiskAssertionScript(IDiskAssertionSettings assertSettings, IPartitionSettings partitionSettings)
  {
    return assertSettings switch
    {
      SkipDiskAssertionSettings => [],
      ScriptDiskAssertionsSettings script => Util.SplitLines(script.Script),
      GeneratedDiskAssertionsSettings generated => GetDiskAssertionScript(generated, partitionSettings),
      _ => throw new NotSupportedException()
    };
  }

  internal static List<string> GetDiskAssertionScript(GeneratedDiskAssertionsSettings settings, IPartitionSettings partitionSettings)
  {
    int targetDisk;
    {
      switch (partitionSettings)
      {
        case UnattendedPartitionSettings ups:
          targetDisk = ups.TargetDisk;
          break;
        case CustomPartitionSettings cps:
          MatchCollection matches = Regex.Matches(cps.Script, @"^(\s*)SELECT(\s+)DISK(\s+|\s*=\s*?)(?<disk>\d+)(\s*)$", RegexOptions.ExplicitCapture | RegexOptions.Multiline | RegexOptions.IgnoreCase);
          if (matches.Count != 1)
          {
            throw new ConfigurationException("Cannot determine target disk from diskpart script. Make sure to include statement such as ‘SELECT DISK=0’.");
          }
          targetDisk = int.Parse(matches[0].Groups["disk"].Value);
          break;
        default:
          throw new NotSupportedException();
      }
    }

    StringWriter writer = new();
    writer.WriteLine($"""
      Function Fail(message)
        WScript.Echo message & " Windows Setup will halt to avoid potential data loss."
        WScript.Quit 1
      End Function

      On Error Resume Next
      Set wmi = GetObject("winmgmts:\\.\root\cimv2")
      Set drive = wmi.Get("Win32_DiskDrive.DeviceID='\\.\PHYSICALDRIVE{targetDisk}'")
      If Err.Number <> 0 Then
        Fail "Could not locate disk {targetDisk} (" & Err.Description & ")."
      End If
      """
    );
    if (settings.AssertInterfaceType)
    {
      writer.WriteLine($"""
        actual = drive.InterfaceType
        If actual <> "IDE" And actual <> "SCSI" Then
          Fail "InterfaceType '" & actual & "' of disk {targetDisk} is unexpected."
        End If
        """);
    }
    if (settings.AssertMediaType)
    {
      writer.WriteLine($"""
        actual = drive.MediaType
        If actual <> "Fixed hard disk media" Then
          Fail "MediaType '" & actual & "' of disk {targetDisk} is unexpected."
        End If
        """);
    }
    if (settings.MinSizeGiB != null)
    {
      writer.WriteLine($"""
        actual = CInt( drive.Size / 1024 / 1024 / 1024 )
        expected = {settings.MinSizeGiB}
        If actual < expected Then
          Fail "Size of disk {targetDisk} is expected to be at least " & expected & " GiB, but actually is " & actual & " GiB."
        End If
        """);
    }
    if (settings.MaxSizeGiB != null)
    {
      writer.WriteLine($"""
        actual = CInt( drive.Size / 1024 / 1024 / 1024 )
        expected = {settings.MaxSizeGiB}
        If actual > expected Then
          Fail "Size of disk {targetDisk} is expected to be at most " & expected & " GiB, but actually is " & actual & " GiB."
        End If
        """);
    }
    if (settings.AssertNoPartitions)
    {
      writer.WriteLine($"""
        actual = drive.Partitions
        If actual > 0 Then
          Fail "There are already " & actual & " partitions on disk {targetDisk}."
        End If
        """);
    }
    writer.WriteLine("""
      WScript.Quit 0
      """);

    return Util.SplitLines(writer.ToString());
  }

  internal static List<string> GetPEScript(Configuration configuration, GeneratePESettings pe, UnattendGenerator generator)
  {
    StringWriter writer = new();

    char[] letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
    const char bootDrive = 'S';
    const char windowsDrive = 'W';
    const char recoveryDrive = 'R';
    char[] skippedDrives = ['A', 'B'];

    bool IncludeSecondaryFile(string path, IEnumerable<string> lines)
    {
      if (lines.Any())
      {
        writer.WriteLine($"@>{path} (");
        foreach (string line in EchoProcessor.Process(lines))
        {
          writer.WriteLine($"\t{line}");
        }
        writer.WriteLine(")");
        return true;
      }
      else
      {
        return false;
      }
    }

    {
      if (configuration.LanguageSettings is UnattendedLanguageSettings settings)
      {
        var pair = settings.LocaleAndKeyboard;
        writer.WriteLine($"""
	        rem Set keyboard layout
	        wpeutil.exe SetKeyboardLayout {pair.Locale.LCID}:{pair.Keyboard.Id}

	        """);
      }
    }

    writer.WriteLine($"""
      @for %%d in ({letters.Except([.. skippedDrives, bootDrive, windowsDrive, recoveryDrive]).JoinString(' ')}) do @(
          if exist %%d:\sources\install.wim set "IMAGE_FILE=%%d:\sources\install.wim"
          if exist %%d:\sources\install.esd set "IMAGE_FILE=%%d:\sources\install.esd"
          if exist %%d:\sources\install.swm set "IMAGE_FILE=%%d:\sources\install.swm" & set "SWM_PARAM=/SWMFile:%%d:\sources\install*.swm"
          if exist %%d:\autounattend.xml set "XML_FILE=%%d:\autounattend.xml"
          if exist %%d:\$OEM$ set "OEM_FOLDER=%%d:\$OEM$"
          if exist %%d:\$WinPEDriver$ set "PEDRIVERS_FOLDER=%%d:\$WinPEDriver$"
      """);
    if (configuration.VirtIoGuestTools)
    {
      writer.WriteLine($"""
	      if exist %%d:\virtio-win-guest-tools.exe set "VIRTIO_DRIVE=%%d:"
	      """);
    }
    writer.WriteLine($"""
      )
      for /f "tokens=3" %%t in ('reg.exe query HKLM\System\Setup /v UnattendFile 2^>nul') do ( if exist %%t set "XML_FILE=%%t" )
      @if not defined IMAGE_FILE echo Could not locate install.wim, install.esd or install.swm. & pause & exit /b 1
      @if not defined XML_FILE echo Could not locate autounattend.xml. & pause & exit /b 1
  
      """);

    writer.WriteLine("""
      set "OS_VERSION=11"
      for /f "tokens=3 delims=." %%v in ('ver') do (
          if %%v LSS 20000 set "OS_VERSION=10"
      )

      """);

    writer.WriteLine("""
      rem Install drivers from $WinPEDriver$ folder
      if defined PEDRIVERS_FOLDER (
          for /R %PEDRIVERS_FOLDER% %%f IN (*.inf) do drvload.exe "%%f"
      )

      """);

    if (configuration.VirtIoGuestTools)
    {
      writer.WriteLine("""
        rem Install VirtIO drivers
        if defined VIRTIO_DRIVE (
            drvload.exe "%VIRTIO_DRIVE%\vioscsi\w%OS_VERSION%\%PROCESSOR_ARCHITECTURE%\vioscsi.inf"
            drvload.exe "%VIRTIO_DRIVE%\NetKVM\w%OS_VERSION%\%PROCESSOR_ARCHITECTURE%\netkvm.inf"
        )

        """);
    }

    {
      List<string> assertScript = GetDiskAssertionScript(pe.DiskAssertionSettings, pe.PartitionSettings);
      if (IncludeSecondaryFile(Paths.AssertScript, assertScript))
      {
        writer.WriteLine($"""
	        cscript.exe //E:vbscript "{Paths.AssertScript}" || ( pause & exit /b 1 )

	        """);
      }
    }

    {
      List<string> diskpartScript = pe.PartitionSettings switch
      {
        CustomPartitionSettings settings => Util.SplitLines(settings.Script),
        UnattendedPartitionSettings settings => GetDiskpartScript(settings, bootDrive: bootDrive, windowsDrive: windowsDrive, recoveryDrive: recoveryDrive),
        _ => throw new NotSupportedException(),
      };
      {
        void CheckDriveLetterAssignment(char letter, string purpose)
        {
          Regex regex = new(@$"^\s*ASSIGN\s+LETTER(\s+|(\s*=\s*))(({letter})|(""{letter}""))\s*$", RegexOptions.IgnoreCase);
          if (!diskpartScript.Any(regex.IsMatch))
          {
            throw new ConfigurationException($"Your diskpart script must contain a line such as ‘ASSIGN LETTER={letter}’ to assign the drive letter ‘{letter}:’ to the {purpose} partition.");
          }
        }
        CheckDriveLetterAssignment(windowsDrive, "Windows");
        CheckDriveLetterAssignment(bootDrive, "system");
      }
      IncludeSecondaryFile(Paths.DiskpartScript, diskpartScript);

      if (pe.PauseBeforeFormatting)
      {
        writer.WriteLine("""
        @echo diskpart will now partition and format your disk
        pause
        """);
      }
      writer.WriteLine($"""
      diskpart.exe /s {Paths.DiskpartScript} || ( echo diskpart.exe encountered an error. & pause & exit /b 1 )
      
      """);
    }

    switch (pe.InstallFromSettings)
    {
      case IndexInstallFromSettings indexSettings:
        writer.WriteLine($"""
          set "IMG_PARAM=/Index:{indexSettings.Index}"
          """);
        break;

      case NameInstallFromSettings nameSettings:
        writer.WriteLine($"""
          set "IMG_PARAM=/Name:"{nameSettings.Name}""
          """);
        break;

      case AutomaticInstallFromSettings:

        string? GetEdition()
        {
          if (configuration.EditionSettings is UnattendedEditionSettings ues)
          {
            return ues.Edition.DisplayName;
          }
          if (configuration.EditionSettings is CustomEditionSettings ces)
          {
            foreach (WindowsEdition we in generator.WindowsEditions.Values)
            {
              if (string.Equals(we.ProductKey, ces.ProductKey, StringComparison.OrdinalIgnoreCase))
              {
                return we.DisplayName;
              }
            }
          }

          return null;
        }

        switch (GetEdition())
        {
          case null:
            throw new ConfigurationException("Cannot determine which Windows image to apply. Specify image name or index in the ‘Windows image to install’ section.");
          case string edition:
            writer.WriteLine($"""
              set "IMG_PARAM=/Name:"Windows %OS_VERSION% {edition}""
              """);
            break;
          default:
            throw new NotSupportedException();
        }
        break;
      default:
        throw new NotSupportedException();
    }

    writer.WriteLine($"""
      dism.exe /Apply-Image /ImageFile:%IMAGE_FILE% %SWM_PARAM% %IMG_PARAM% /ApplyDir:{windowsDrive}:\ {(pe.CompactOs ? "/Compact " : " ")}/CheckIntegrity /Verify || ( echo dism.exe encountered an error. & pause & exit /b 1 )
    
      bcdboot.exe {windowsDrive}:\Windows /s {bootDrive}: || ( echo bcdboot.exe encountered an error. & pause & exit /b 1 )

      """);

    {
      void DeleteWinRE()
      {
        writer.WriteLine($"""
        rem Avoid creation of recovery partition
        del {windowsDrive}:\Windows\System32\Recovery\winre.wim
        
        """);
      }

      switch (pe.PartitionSettings)
      {
        case UnattendedPartitionSettings settings:
          switch (settings.RecoveryMode)
          {
            case RecoveryMode.None:
              DeleteWinRE();
              break;
            case RecoveryMode.Partition:
              // Nothing to do – Windows will automatically install RE on the recovery partition
              break;
            default:
              throw new NotSupportedException();
          }
          break;
        case CustomPartitionSettings settings:
          string[] keywords = [
            "SET ID=27",
            @"LABEL=""Recovery""",
            @"SET ID=""de94bba4-06d1-4d40-a16a-bfd50179d6ac"""
            ];
          if (!keywords.Any(keyword => settings.Script.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
          {
            DeleteWinRE();
          }
          break;
      }
    }

    writer.WriteLine($"""
      mkdir {windowsDrive}:\Windows\Panther
      copy %XML_FILE% {windowsDrive}:\Windows\Panther\unattend.xml
    
      """);

    writer.WriteLine($"""
      if defined PEDRIVERS_FOLDER (
          dism.exe /Add-Driver /Image:{windowsDrive}:\ /Driver:"%PEDRIVERS_FOLDER%" /Recurse
      )

      """);

    if (configuration.VirtIoGuestTools)
    {
      writer.WriteLine($"""
        if defined VIRTIO_DRIVE (
            dism.exe /Add-Driver /Image:{windowsDrive}:\ /Driver:"%VIRTIO_DRIVE%\vioscsi\w%OS_VERSION%\%PROCESSOR_ARCHITECTURE%\vioscsi.inf"
            dism.exe /Add-Driver /Image:{windowsDrive}:\ /Driver:"%VIRTIO_DRIVE%\NetKVM\w%OS_VERSION%\%PROCESSOR_ARCHITECTURE%\netkvm.inf"
        )

        """);
    }

    {
      if (configuration.TimeZoneSettings is ExplicitTimeZoneSettings settings)
      {
        writer.WriteLine($"""
          dism.exe /Image:{windowsDrive}:\ /Set-TimeZone:"{settings.TimeZone.Id}"

          """);
      }
    }

    if (pe.Disable8Dot3Names)
    {
      writer.WriteLine($"""
        rem Strip 8.3 file names
        fsutil.exe 8dot3name set {windowsDrive}: 1
        fsutil.exe 8dot3name strip /s /f {windowsDrive}:\

        """);
    }

    if (pe.DisableDefender)
    {
      writer.WriteLine($"""
        rem Disable Windows Defender
        reg.exe LOAD HKLM\mount {windowsDrive}:\Windows\System32\config\SYSTEM
        for %%s in (Sense WdBoot WdFilter WdNisDrv WdNisSvc WinDefend) do reg.exe ADD HKLM\mount\ControlSet001\Services\%%s /v Start /t REG_DWORD /d 4 /f
        reg.exe UNLOAD HKLM\mount

        """);
    }

    if (configuration.DisableWpbt)
    {
      writer.WriteLine($"""
        rem Disable WPBT
        reg.exe LOAD HKLM\mount {windowsDrive}:\Windows\System32\config\SYSTEM
        reg.exe add "HKLM\mount\ControlSet001\Control\Session Manager" /v DisableWpbtExecution /t REG_DWORD /d 1 /f
        reg.exe UNLOAD HKLM\mount

        """);
    }

    {
      if (configuration.LanguageSettings is UnattendedLanguageSettings settings)
      {
        GeoLocation location = settings.GeoLocation;
        writer.WriteLine($"""
          rem Set device setup region to {location.DisplayName} (GeoID {location.Id})
          reg.exe LOAD HKLM\mount {windowsDrive}:\Windows\System32\config\SOFTWARE
          reg.exe ADD "HKLM\mount\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion" /v DeviceRegion /t REG_DWORD /d {location.Id} /f
          reg.exe UNLOAD HKLM\mount

          """);
      }
    }

    if (configuration.UseConfigurationSet)
    {
      writer.WriteLine($"""
        rem Copy $OEM$ folder if present
        set "ROBOCOPY_ARGS=/E /XX /COPY:DAT /DCOPY:DAT /R:0"
        if defined OEM_FOLDER (
            if exist "%OEM_FOLDER%\$$" robocopy.exe "%OEM_FOLDER%\$$" {windowsDrive}:\Windows %ROBOCOPY_ARGS%
            if exist "%OEM_FOLDER%\$1" robocopy.exe "%OEM_FOLDER%\$1" {windowsDrive}:\ %ROBOCOPY_ARGS%
            @for %%d in ({letters.Except(skippedDrives).JoinString(' ')}) do @(
                if exist "%OEM_FOLDER%\%%d" robocopy.exe "%OEM_FOLDER%\%%d" %%d:\ %ROBOCOPY_ARGS%
            )
        )

        """);
    }

    if (pe.PauseBeforeReboot)
    {
      writer.WriteLine("""
        @echo Computer will now reboot
        pause

        """);
    }

    writer.WriteLine("""
      rem Continue with next stage of Windows Setup after reboot
      wpeutil.exe reboot

      """);

    return Util.SplitLines(writer.ToString());
  }
}
