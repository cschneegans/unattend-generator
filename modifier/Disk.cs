﻿using System;
using System.Collections.Generic;

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

public record class ScriptPESetttings(
  string Script
) : IPESettings;

class DiskModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.PESettings is ScriptPESetttings settings)
    {
      string file = @"X:\pe.cmd";

      CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
      appender.Append([
        ..CommandBuilder.WriteToFile(file, Util.SplitLines(settings.Script)),
        CommandBuilder.ShellCommand(file)
      ]);
      return;
    }

    AssertDisk();
    SetCompactMode();
    SetPartitions();
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
      string file = @"X:\assert.vbs";

      CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
      appender.Append([
        ..CommandBuilder.WriteToFile(file, lines),
        CommandBuilder.InvokeVBScript(file)
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
      string script = @"X:\diskpart.txt";
      string log = @"X:\diskpart.log";

      CommandAppender appender = GetAppender(CommandConfig.WindowsPE);
      appender.Append([
        ..CommandBuilder.WriteToFile(script, lines),
        CommandBuilder.ShellCommand($@"diskpart.exe /s ""{script}"" >>""{log}"" || ( type ""{log}"" & echo diskpart encountered an error. & pause & exit /b 1 )"),
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

  static List<string> GetDiskpartScript(UnattendedPartitionSettings settings)
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
        AddIf("ACTIVE");
        AddIf("CREATE PARTITION PRIMARY");
        AddIf($"SHRINK MINIMUM={settings.RecoverySize}", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Windows""");
        AddIf("CREATE PARTITION PRIMARY", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Recovery""", recoveryPartition);
        AddIf("SET ID=27", recoveryPartition);
        break;

      case PartitionLayout.GPT:
        AddIf("SELECT DISK=0");
        AddIf("CLEAN");
        AddIf("CONVERT GPT");
        AddIf($"CREATE PARTITION EFI SIZE={settings.EspSize}");
        AddIf(@"FORMAT QUICK FS=FAT32 LABEL=""System""");
        AddIf("CREATE PARTITION MSR SIZE=16");
        AddIf("CREATE PARTITION PRIMARY");
        AddIf($"SHRINK MINIMUM={settings.RecoverySize}", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Windows""");
        AddIf("CREATE PARTITION PRIMARY", recoveryPartition);
        AddIf(@"FORMAT QUICK FS=NTFS LABEL=""Recovery""", recoveryPartition);
        AddIf(@"SET ID=""de94bba4-06d1-4d40-a16a-bfd50179d6ac""", recoveryPartition);
        AddIf("GPT ATTRIBUTES=0x8000000000000001", recoveryPartition);
        break;
    }

    return lines;
  }
}
