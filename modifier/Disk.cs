using System;
using System.Collections.Generic;

namespace Schneegans.Unattend;

public interface IPartitionSettings;

public class InteractivePartitionSettings : IPartitionSettings;

public interface IInstallToSettings;

public class AvailableInstallToSettings : IInstallToSettings;

public record class CustomInstallToSettings(
  int InstallToDisk,
  int InstallToPartition
) : IInstallToSettings;

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

class DiskModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    switch (Configuration.PartitionSettings)
    {
      case InteractivePartitionSettings:
        {
          Document.SelectSingleNodeOrThrow("//u:ImageInstall", NamespaceManager).RemoveSelf();
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
            CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Specialize);
            appender.Command(@"ReAgentc.exe /disable");
            appender.ShellCommand(@"del /a /f ""C:\Windows\System32\Recovery\Winre.wim""");
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
  }

  private void InstallTo(int disk, int partition)
  {
    Document.SelectSingleNodeOrThrow("//u:ImageInstall/u:OSImage/u:InstallTo/u:DiskID", NamespaceManager).InnerText = disk.ToString();
    Document.SelectSingleNodeOrThrow("//u:ImageInstall/u:OSImage/u:InstallTo/u:PartitionID", NamespaceManager).InnerText = partition.ToString();
  }

  private void WriteScript(IEnumerable<string> lines)
  {
    CommandAppender appender = new(Document, NamespaceManager, CommandConfig.WindowsPE);

    string filename = @"X:\diskpart.txt";
    string logfile = @"X:\diskpart.log";

    appender.WriteToFile(filename, lines);
    appender.ShellCommand($@">>""{logfile}"" diskpart.exe /s ""{filename}""");
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
