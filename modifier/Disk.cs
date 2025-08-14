using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Schneegans.Unattend;

public interface IPartitionSettings;

public class InteractivePartitionSettings : IPartitionSettings;

public interface IInstallToSettings;

public class AvailableInstallToSettings : IInstallToSettings;

public class CustomInstallToSettings(
  int installToDisk,
  int installToPartition
) : IInstallToSettings
{
  public int InstallToDisk => Validation.InRange(installToDisk, min: 0);

  public int InstallToPartition => Validation.InRange(installToPartition, min: 1);
}

public record class CustomPartitionSettings(
  string Script,
  IInstallToSettings InstallTo
) : IPartitionSettings;

public record class UnattendedPartitionSettings(
  PartitionLayout PartitionLayout,
  RecoveryMode RecoveryMode,
  int EspSize = Constants.EspDefaultSize,
  int RecoverySize = Constants.RecoveryPartitionSize
) : IPartitionSettings;

public enum CompactOsModes
{
  Default, Always, Never
}

public interface IDiskAssertionSettings;

public class SkipDiskAssertionSettings : IDiskAssertionSettings;

public record class ScriptDiskAssertionsSettings(
  string Script
) : IDiskAssertionSettings;

public interface IPESettings;

public class DefaultPESettings : IPESettings;

public interface ICmdPESettings : IPESettings;

public record class GeneratePESettings(
  bool Disable8Dot3Names,
  bool PauseBeforeFormatting,
  bool PauseBeforeReboot
) : ICmdPESettings;

public record class ScriptPESetttings(
  string Script
) : ICmdPESettings;

record class ImageSpec(
  string Key,
  string Value,
  bool PrependOsVersion
);

static class Paths
{
  static internal readonly string PEScript = @"X:\pe.cmd";
  static internal readonly string DiskpartScript = @"X:\diskpart.txt";
  static internal readonly string DiskpartLog = @"X:\diskpart.log";
  static internal readonly string AssertionScript = @"X:\assert.vbs";
}

class DiskModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.PESettings is ICmdPESettings)
    {
      foreach (var node in Document.SelectNodesOrEmpty($"/u:unattend/u:settings[@pass='{Pass.windowsPE}']/*", NamespaceManager))
      {
        node.RemoveSelf();
      }
    }

    AssertDisk();

    switch (Configuration.PESettings)
    {
      case ScriptPESetttings peSettings:
        WritePeScript(Util.SplitLines(peSettings.Script));
        break;

      case GeneratePESettings peSettings:
        {
          var writer = new StringWriter();

          char[] letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
          const char bootDrive = 'S';
          const char windowsDrive = 'W';
          const char recoveryDrive = 'R';

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

          {
            if (Configuration.LanguageSettings is UnattendedLanguageSettings settings)
            {
              var pair = settings.LocaleAndKeyboard;
              writer.WriteLine("rem Set keyboard layout");
              writer.WriteLine($"wpeutil.exe SetKeyboardLayout {pair.Locale.LCID}:{pair.Keyboard.Id}");
            }
          }

          writer.WriteLine($"""
            @for %%d in ({letters.Except(['A', 'B', 'X', bootDrive, windowsDrive, recoveryDrive]).JoinString(' ')}) do @(
                if exist %%d:\sources\install.wim set "IMAGE_FILE=%%d:\sources\install.wim"
                if exist %%d:\sources\install.esd set "IMAGE_FILE=%%d:\sources\install.esd"
                if exist %%d:\sources\install.swm set "IMAGE_FILE=%%d:\sources\install.swm" & set "SWM_PARAM=/SWMFile:%%d:\sources\install*.swm"
                if exist %%d:\autounattend.xml set "XML_FILE=%%d:\autounattend.xml"
                if exist %%d:\$OEM$ set "OEM_FOLDER=%%d:\$OEM$"
            )
            @if not defined IMAGE_FILE echo Could not locate install.wim, install.esd or install.swm. & pause & exit /b 1
            @if not defined XML_FILE echo Could not locate autounattend.xml. & pause & exit /b 1
            """);

          void WriteDiskpartScript(IEnumerable<string> lines)
          {
            CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
            appender.Append(CommandBuilder.WriteToFilePE(Paths.DiskpartScript, lines));
          }

          switch (Configuration.PartitionSettings)
          {
            case InteractivePartitionSettings:
              throw new ConfigurationException("Cannot create .cmd script when disk is partitioned interactively. Select ‘Let Windows Setup wipe, partition and format your hard drive’ or ‘Use a custom diskpart script’ instead.");

            case CustomPartitionSettings settings:
              {
                var lines = Util.SplitLines(settings.Script);
                {
                  string expected = $"ASSIGN LETTER={windowsDrive}";
                  if (!lines.Any(line => string.Equals(line.Trim(), expected, StringComparison.OrdinalIgnoreCase)))
                  {
                    throw new ConfigurationException($"Your diskpart script must contain the line ‘{expected}’ to assign the drive letter ‘{windowsDrive}:’ to the Windows partition.");
                  }
                }
                {
                  string expected = $"ASSIGN LETTER={bootDrive}";
                  if (!lines.Any(line => string.Equals(line.Trim(), expected, StringComparison.OrdinalIgnoreCase)))
                  {
                    throw new ConfigurationException($"Your diskpart script must contain the line ‘{expected}’ to assign the drive letter ‘{bootDrive}:’ to the system partition.");
                  }
                }
                WriteDiskpartScript(lines);
                break;
              }

            case UnattendedPartitionSettings settings:
              {
                var lines = GetDiskpartScript(settings, bootDrive: bootDrive, windowsDrive: windowsDrive, recoveryDrive: recoveryDrive);
                WriteDiskpartScript(lines);
                break;
              }

            default:
              throw new NotSupportedException();
          }

          if (peSettings.PauseBeforeFormatting)
          {
            writer.WriteLine("""
              @echo diskpart will now partition and format your disk
              pause
              """);
          }

          writer.WriteLine($"""
            diskpart.exe /s {Paths.DiskpartScript} || ( echo diskpart.exe encountered an error. & pause & exit /b 1 )
            """);

          ImageSpec GetImageSpec()
          {
            {
              if (Configuration.InstallFromSettings is IndexInstallFromSettings settings)
              {
                return new("Index", settings.Value, false);
              }
            }
            {
              if (Configuration.InstallFromSettings is NameInstallFromSettings settings)
              {
                return new("Name", settings.Value, false);
              }
            }
            {
              if (Configuration.EditionSettings is UnattendedEditionSettings settings)
              {
                return new("Name", settings.Edition.DisplayName, true);
              }
            }
            throw new ConfigurationException("Cannot determine which Windows image to apply. Specify image name or index in the ‘Source image’ section.");
          }

          var image = GetImageSpec();
          if (image.PrependOsVersion)
          {
            writer.WriteLine("""
              set "OS_VERSION=Windows 11 "
              for /f "tokens=3 delims=." %%v in ('ver') do (
                  if %%v LSS 20000 set "OS_VERSION=Windows 10 "
              )
              """);
          }

          writer.WriteLine($"""
            dism.exe /Apply-Image /ImageFile:%IMAGE_FILE% %SWM_PARAM% /{image.Key}:"%OS_VERSION%{image.Value}" /ApplyDir:{windowsDrive}:\ {(Configuration.CompactOsMode == CompactOsModes.Always ? "/Compact" : "")} || ( echo dism.exe encountered an error. & pause & exit /b 1 )
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

            switch (Configuration.PartitionSettings)
            {
              case UnattendedPartitionSettings settings:
                switch (settings.RecoveryMode)
                {
                  case RecoveryMode.None:
                    DeleteWinRE();
                    break;
                  case RecoveryMode.Folder:
                    throw new ConfigurationException($"Cannot create .cmd script when Windows RE is to be installed on {windowsDrive}:.");
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
            copy %XML_FILE% {windowsDrive}:\Windows\Panther\unattend-original.xml
            """);

          if (peSettings.Disable8Dot3Names)
          {
            writer.WriteLine($"""
              rem Strip 8.3 file names
              fsutil.exe 8dot3name set {windowsDrive}: 1
              fsutil.exe 8dot3name strip /s /f {windowsDrive}:\
              """);
          }

          if (Configuration.DisableDefender)
          {
            writer.WriteLine($"""
              rem Disable Windows Defender
              reg.exe LOAD HKLM\mount {windowsDrive}:\Windows\System32\config\SYSTEM
              for %%s in (Sense WdBoot WdFilter WdNisDrv WdNisSvc WinDefend) do reg.exe ADD HKLM\mount\ControlSet001\Services\%%s /v Start /t REG_DWORD /d 4 /f
              reg.exe UNLOAD HKLM\mount
              """);
          }

          if (Configuration.UseConfigurationSet)
          {
            writer.WriteLine($"""
              rem Copy $OEM$ folder if present
              set "ROBOCOPY_ARGS=/E /XX /COPY:DAT /DCOPY:DAT /R:0"
              if defined OEM_FOLDER (
                  if exist "%OEM_FOLDER%\$$" robocopy.exe "%OEM_FOLDER%\$$" {windowsDrive}:\Windows %ROBOCOPY_ARGS%
                  if exist "%OEM_FOLDER%\$1" robocopy.exe "%OEM_FOLDER%\$1" {windowsDrive}:\ %ROBOCOPY_ARGS%
                  @for %%d in ({letters.Except(['A', 'B', 'X']).JoinString(' ')}) do @(
                      if exist "%OEM_FOLDER%\%%d" robocopy.exe "%OEM_FOLDER%\%%d" %%d:\ %ROBOCOPY_ARGS%
                  )
              )
              """);
          }

          if (peSettings.PauseBeforeReboot)
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

          WritePeScript(Util.SplitLines(writer.ToString()));
          break;
        }

      case DefaultPESettings:
        SetCompactMode();
        SetPartitions();
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

  private void AssertDisk()
  {
    switch (Configuration.DiskAssertionSettings)
    {
      case SkipDiskAssertionSettings:
        break;
      case ScriptDiskAssertionsSettings settings:
        WriteScript(Util.SplitLines(settings.Script));
        break;
    }

    void WriteScript(IEnumerable<string> lines)
    {
      CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
      appender.Append([
        ..CommandBuilder.WriteToFilePE(Paths.AssertionScript, lines),
        CommandBuilder.InvokeVBScript(Paths.AssertionScript)
      ]);
    }
  }

  private void SetCompactMode()
  {
    var target = Document.SelectSingleNodeOrThrow("//u:Compact", NamespaceManager);
    switch (Configuration.CompactOsMode)
    {
      case CompactOsModes.Default:
        target.RemoveSelf();
        break;
      case CompactOsModes.Always:
        target.InnerText = "true";
        break;
      case CompactOsModes.Never:
        target.InnerText = "false";
        break;
    }
  }

  private void SetPartitions()
  {
    switch (Configuration.PartitionSettings)
    {
      case InteractivePartitionSettings:
        {
          Document.SelectSingleNodeOrThrow("//u:InstallTo", NamespaceManager).RemoveSelf();
          break;
        }
      case UnattendedPartitionSettings settings:
        {
          WriteScript(GetDiskpartScript(settings));
          {
            int partition = settings.PartitionLayout switch
            {
              PartitionLayout.MBR => 2,
              PartitionLayout.GPT => 3,
              _ => throw new ArgumentException(nameof(settings.PartitionLayout)),
            };
            InstallTo(disk: 0, partition: partition);
          }

          if (settings.RecoveryMode == RecoveryMode.None)
          {
            SpecializeScript.Append("""
              ReAgentc.exe /disable;
              Remove-Item -LiteralPath 'C:\Windows\System32\Recovery\Winre.wim' -Force -ErrorAction 'SilentlyContinue';
              """);
          }
          break;
        }

      case CustomPartitionSettings settings:
        {
          WriteScript(Util.SplitLines(settings.Script));
          switch (settings.InstallTo)
          {
            case AvailableInstallToSettings:
              {
                Document.SelectSingleNodeOrThrow("//u:ImageInstall/u:OSImage/u:InstallTo", NamespaceManager).RemoveSelf();
                var elem = Document.CreateElement("InstallToAvailablePartition", NamespaceManager.LookupNamespace("u"));
                elem.InnerText = "true";
                Document.SelectSingleNodeOrThrow("//u:ImageInstall/u:OSImage", NamespaceManager).AppendChild(elem);
                break;
              }
            case CustomInstallToSettings custom:
              {
                InstallTo(disk: custom.InstallToDisk, partition: custom.InstallToPartition);
                break;
              }
            default:
              {
                throw new NotSupportedException();
              }
          }
          break;
        }
      default:
        {
          throw new NotSupportedException();
        }
    }

    void WriteScript(IEnumerable<string> lines)
    {
      CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
      appender.Append([
        ..CommandBuilder.WriteToFilePE(Paths.DiskpartScript, lines),
        CommandBuilder.ShellCommand($@"diskpart.exe /s ""{Paths.DiskpartScript}"" >>""{Paths.DiskpartLog}"" || ( type ""{Paths.DiskpartLog}"" & echo diskpart encountered an error. & pause & exit /b 1 )"),
      ]);
    }
  }

  private void InstallTo(int disk, int partition)
  {
    Document.SelectSingleNodeOrThrow("//u:ImageInstall/u:OSImage/u:InstallTo/u:DiskID", NamespaceManager).InnerText = disk.ToString();
    Document.SelectSingleNodeOrThrow("//u:ImageInstall/u:OSImage/u:InstallTo/u:PartitionID", NamespaceManager).InnerText = partition.ToString();
  }

  public static string GetCustomDiskpartScript()
  {
    UnattendedPartitionSettings settings = new(
      PartitionLayout: PartitionLayout.GPT,
      RecoveryMode: RecoveryMode.Partition,
      EspSize: Constants.EspDefaultSize,
      RecoverySize: Constants.RecoveryPartitionSize
    );
    return string.Join("\r\n", GetDiskpartScript(settings));
  }

  internal static List<string> GetDiskpartScript(UnattendedPartitionSettings settings, char bootDrive = 'S', char windowsDrive = 'W', char recoveryDrive = 'R')
  {
    List<string> lines = [];

    void AddIf(string item, bool condition = true)
    {
      if (condition)
      {
        lines.Add(item);
      }
    }

    bool recoveryPartition = settings.RecoveryMode == RecoveryMode.Partition;

    switch (settings.PartitionLayout)
    {
      case PartitionLayout.MBR:
        AddIf("SELECT DISK=0");
        AddIf("CLEAN");
        AddIf("CREATE PARTITION PRIMARY SIZE=100");
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""System Reserved""");
        AddIf($"ASSIGN LETTER={bootDrive}");
        AddIf("ACTIVE");
        AddIf("CREATE PARTITION PRIMARY");
        AddIf($"SHRINK MINIMUM={settings.RecoverySize}", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Windows""");
        AddIf($"ASSIGN LETTER={windowsDrive}");
        AddIf("CREATE PARTITION PRIMARY", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Recovery""", recoveryPartition);
        AddIf($"ASSIGN LETTER={recoveryDrive}", recoveryPartition);
        AddIf("SET ID=27", recoveryPartition);
        break;

      case PartitionLayout.GPT:
        AddIf("SELECT DISK=0");
        AddIf("CLEAN");
        AddIf("CONVERT GPT");
        AddIf($"CREATE PARTITION EFI SIZE={settings.EspSize}");
        AddIf(@"FORMAT QUICK FS=FAT32 LABEL=""System""");
        AddIf($"ASSIGN LETTER={bootDrive}");
        AddIf("CREATE PARTITION MSR SIZE=16");
        AddIf("CREATE PARTITION PRIMARY");
        AddIf($"SHRINK MINIMUM={settings.RecoverySize}", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Windows""");
        AddIf($"ASSIGN LETTER={windowsDrive}");
        AddIf("CREATE PARTITION PRIMARY", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Recovery""", recoveryPartition);
        AddIf($"ASSIGN LETTER={recoveryDrive}", recoveryPartition);
        AddIf(@"SET ID=""de94bba4-06d1-4d40-a16a-bfd50179d6ac""", recoveryPartition);
        AddIf("GPT ATTRIBUTES=0x8000000000000001", recoveryPartition);
        break;
    }

    return lines;
  }
}
