using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Schneegans.Unattend;

public enum ScriptType
{
  Cmd, Ps1, Reg, Vbs, Js
}

public enum ScriptPhase
{
  /// <summary>
  /// Script is to run in the system context, before user accounts are created.
  /// </summary>
  System,

  /// <summary>
  /// Script is to run when the first user logs on.
  /// </summary>
  FirstLogon,

  /// <summary>
  /// Script is to run whenever a user logs on for the first time.
  /// </summary>
  UserOnce,

  /// <summary>
  /// Script is to modify the default user's registry hive.
  /// </summary>
  DefaultUser
}

public static class ScriptExtensions
{
  public static IEnumerable<ScriptType> GetAllowedTypes(this ScriptPhase phase)
  {
    return phase switch
    {
      ScriptPhase.DefaultUser => [
        ScriptType.Reg,
        ScriptType.Cmd,
        ScriptType.Ps1,
      ],
      _ => Enum.GetValues<ScriptType>(),
    };
  }

  public static string DefaultUserRequiredPrefix(this ScriptType type)
  {
    return type switch
    {
      ScriptType.Reg => @"[HKEY_USERS\DefaultUser\",
      ScriptType.Cmd => @"HKU\DefaultUser\",
      ScriptType.Ps1 => @"Registry::HKU\DefaultUser\",
      _ => "",
    };
  }

  public static string FileExtension(this ScriptType type)
  {
    return '.' + type.ToString().ToLowerInvariant();
  }
}

public record class ScriptSettings(
  IEnumerable<Script> Scripts
);

public class Script
{
  public Script(string content, ScriptPhase phase, ScriptType type)
  {
    if (!phase.GetAllowedTypes().Contains(type))
    {
      throw new ConfigurationException($"Scripts in phase '{phase}' must not have type '{type}'.");
    }

    if (phase == ScriptPhase.DefaultUser && !string.IsNullOrWhiteSpace(content))
    {
      string prefix = type.DefaultUserRequiredPrefix();
      if (!content.Contains(prefix, StringComparison.OrdinalIgnoreCase))
      {
        throw new ConfigurationException($"{type.FileExtension()} script '{content}' does not contain required key prefix '{prefix}'.");
      }
    }

    Content = content;
    Phase = phase;
    Type = type;
  }

  public string Content { get; }

  public ScriptPhase Phase { get; }

  public ScriptType Type { get; }
}

public record class ScriptInfo(Script Script, string ScriptPath, string LogPath, string Key)
{
  public static ScriptInfo Create(Script script, int index)
  {
    const string folder = @"C:\Windows\Setup\Scripts";
    string name = $"unattend-{index + 1:x2}";
    string extension = script.Type.ToString().ToLowerInvariant();
    return new ScriptInfo(
      Script: script,
      ScriptPath: @$"{folder}\{name}.{extension}",
      LogPath: @$"{folder}\{name}.log",
      Key: name
    );
  }
}

class ScriptModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    var infos = Configuration.ScriptSettings.Scripts.Select(ScriptInfo.Create).ToImmutableList();
    if (infos.IsEmpty)
    {
      return;
    }

    foreach (var info in infos)
    {
      WriteScriptContent(info);
      CallScript(info);
    }
  }

  private void WriteScriptContent(ScriptInfo info)
  {
    static string Clean(Script script)
    {
      if (script.Type == ScriptType.Reg)
      {
        string prefix = "Windows Registry Editor Version 5.00";
        if (!script.Content.StartsWith(prefix))
        {
          return $"{prefix}\r\n\r\n{script.Content}";
        }
      }
      return script.Content;
    }

    AddTextFile(Clean(info.Script), info.ScriptPath);
  }

  private void CallScript(ScriptInfo info)
  {
    CommandAppender appender = GetAppender(info.Script.Phase switch
    {
      ScriptPhase.FirstLogon => CommandConfig.Oobe,
      _ => CommandConfig.Specialize,
    });

    string command = CommandHelper.GetCommand(info);

    switch (info.Script.Phase)
    {
      case ScriptPhase.System:
      case ScriptPhase.FirstLogon:
        appender.Append(
          command
        );
        break;
      case ScriptPhase.UserOnce:
        appender.Append(
          CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
          {
            return [CommandBuilder.UserRunOnceCommand(info.Key, command, rootKey, subKey)];
          })
        );
        break;
      case ScriptPhase.DefaultUser:
        appender.Append(
          CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
          {
            return [command];
          })
        );
        break;
      default:
        throw new NotSupportedException();
    }
  }
}

public static class CommandHelper
{
  public static string GetCommand(ScriptInfo info)
  {
    string inner = info.Script.Type switch
    {
      ScriptType.Cmd => CommandBuilder.Raw(info.ScriptPath),
      ScriptType.Ps1 => CommandBuilder.InvokePowerShellScript(info.ScriptPath),
      ScriptType.Reg => CommandBuilder.RegistryCommand(@$"import ""{info.ScriptPath}"""),
      ScriptType.Vbs => CommandBuilder.InvokeVBScript(info.ScriptPath),
      ScriptType.Js => CommandBuilder.InvokeJScript(info.ScriptPath),
      _ => throw new NotSupportedException(),
    };

    if (info.Script.Phase == ScriptPhase.UserOnce)
    {
      return inner;
    }
    else
    {
      return CommandBuilder.ShellCommand(inner, outFile: info.LogPath);
    }
  }
}