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

  public static string DefaultUserKeyPrefix(this ScriptType type)
  {
    return type switch
    {
      ScriptType.Reg => @"[HKEY_USERS\DefaultUser\",
      ScriptType.Cmd => @"HKU\DefaultUser\",
      ScriptType.Ps1 => @"Registry::HKU\DefaultUser\",
      _ => throw new NotSupportedException(),
    };
  }

  public static string FileExtension(this ScriptType type)
  {
    return '.' + type.ToString().ToLowerInvariant();
  }
}

public record class ScriptSettings(
  IEnumerable<Script> Scripts,
  bool RestartExplorer
);

public class Script
{
  public Script(string content, ScriptPhase phase, ScriptType type)
  {
    if (!phase.GetAllowedTypes().Contains(type))
    {
      throw new ConfigurationException($"Scripts in phase '{phase}' must not have type '{type}'.");
    }

    Content = content;
    Phase = phase;
    Type = type;
  }

  public string Content { get; }

  public ScriptPhase Phase { get; }

  public ScriptType Type { get; }
}

public record class ScriptInfo(Script Script, string FilePath, string FileName, string Key)
{
  public static ScriptInfo Create(Script script, int index)
  {
    const string folder = @"C:\Windows\Setup\Scripts";
    string key = $"unattend-{index + 1:x2}";
    string extension = script.Type.ToString().ToLowerInvariant();
    string fileName = $"{key}.{extension}";
    return new ScriptInfo(
      Script: script,
      FilePath: @$"{folder}\{fileName}",
      FileName: fileName,
      Key: key
    );
  }
}

class ScriptModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.ScriptSettings.RestartExplorer)
    {
      UserOnceScript.RestartExplorer();
    }

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

    EmbedTextFile(info.FileName, Clean(info.Script));
  }

  private void CallScript(ScriptInfo info)
  {
    string command = CommandHelper.GetCommand(info, CommandBuilder);

    void AppendPowerShellSequence(PowerShellSequence sequence)
    {
      if (info.Script.Type == ScriptType.Ps1)
      {
        sequence.InvokeFile(info.FilePath);
      }
      else
      {
        sequence.Append(command + ";");
      }
    }

    switch (info.Script.Phase)
    {
      case ScriptPhase.System:
        AppendPowerShellSequence(SpecializeScript);
        break;
      case ScriptPhase.FirstLogon:
        AppendPowerShellSequence(FirstLogonScript);
        break;
      case ScriptPhase.UserOnce:
        AppendPowerShellSequence(UserOnceScript);
        break;
      case ScriptPhase.DefaultUser:
        AppendPowerShellSequence(DefaultUserScript);
        break;
      default:
        throw new NotSupportedException();
    }
  }
}

public static class CommandHelper
{
  public static string GetCommand(ScriptInfo info, CommandBuilder builder)
  {
    return info.Script.Type switch
    {
      ScriptType.Cmd => builder.Raw(info.FilePath),
      ScriptType.Ps1 => builder.InvokePowerShellScript(info.FilePath),
      ScriptType.Reg => builder.RegistryCommand(@$"import ""{info.FilePath}"""),
      ScriptType.Vbs => builder.InvokeVBScript(info.FilePath),
      ScriptType.Js => builder.InvokeJScript(info.FilePath),
      _ => throw new NotSupportedException(),
    };
  }
}