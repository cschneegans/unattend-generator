using System;
using System.Collections.Generic;
using System.Linq;

namespace Schneegans.Unattend;

public enum ScriptType
{
  Cmd, Ps1, Reg, Vbs, Js
}

public enum ScriptPhase
{
  System, FirstLogon, UserOnce, DefaultUser
}

public static class ScriptExtensions
{
  public static IEnumerable<ScriptType> GetAllowedTypes(this ScriptPhase phase)
  {
    return phase switch
    {
      ScriptPhase.DefaultUser => [ScriptType.Reg],
      _ => Enum.GetValues<ScriptType>(),
    };
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

    Content = content;
    Phase = phase;
    Type = type;
  }

  public string Content { get; }

  public ScriptPhase Phase { get; }

  public ScriptType Type { get; }
}

class ScriptModifier(ModifierContext context) : Modifier(context)
{
  private int count = 0;

  private const string ScriptsDirectory = @"C:\Windows\Setup\Scripts";

  private bool directoryCreated = false;

  public override void Process()
  {
    foreach (Script script in Configuration.ScriptSettings.Scripts)
    {
      if (!string.IsNullOrWhiteSpace(script.Content))
      {
        ScriptId scriptId = NewScriptId(script);
        CreateScriptsDirectoryOnce();
        WriteScriptContent(script, scriptId);
        CallScript(script, scriptId);
      }
    }
  }

  record class ScriptId(string FullName, string Key);

  private ScriptId NewScriptId(Script script)
  {
    string name = $"unattend-{++count:x2}";
    string extension = script.Type.ToString().ToLowerInvariant();
    return new ScriptId(@$"{ScriptsDirectory}\{name}.{extension}", name);
  }

  private void CreateScriptsDirectoryOnce()
  {
    if (!directoryCreated)
    {
      var appender = new CommandAppender(Document, NamespaceManager, CommandConfig.Specialize);
      appender.Append(
        CommandBuilder.ShellCommand($"mkdir {ScriptsDirectory}")
      );
      directoryCreated = true;
    }
  }

  private void WriteScriptContent(Script script, ScriptId scriptId)
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

    var appender = new CommandAppender(Document, NamespaceManager, CommandConfig.Specialize);
    appender.Append(
      CommandBuilder.SafeWriteToFile(scriptId.FullName, Clean(script))
    );
  }

  private void CallScript(Script script, ScriptId scriptId)
  {
    var appender = new CommandAppender(Document, NamespaceManager, script.Phase switch
    {
      ScriptPhase.FirstLogon => CommandConfig.Oobe,
      _ => CommandConfig.Specialize,
    });

    string command = CommandHelper.GetCommand(script, scriptId.FullName);

    switch (script.Phase)
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
            return [CommandBuilder.UserRunOnceCommand(scriptId.Key, command, rootKey, subKey)];
          })
        );
        break;
      case ScriptPhase.DefaultUser:
        string mountKey = @"""HKU\DefaultUser""";
        appender.Append([
          CommandBuilder.RegistryCommand(@$"load {mountKey} ""C:\Users\Default\NTUSER.DAT"""),
          command,
          CommandBuilder.RegistryCommand($"unload {mountKey}"),
        ]);
        break;
      default:
        throw new NotSupportedException();
    }
  }
}

public static class CommandHelper
{
  public static string GetCommand(Script script, string filepath)
  {
    return script.Type switch
    {
      ScriptType.Cmd => CommandBuilder.Raw(filepath),
      ScriptType.Ps1 => CommandBuilder.InvokePowerShellScript(filepath),
      ScriptType.Reg => CommandBuilder.RegistryCommand(@$"import ""{filepath}"""),
      ScriptType.Vbs => CommandBuilder.InvokeVBScript(filepath),
      ScriptType.Js => CommandBuilder.InvokeJScript(filepath),
      _ => throw new NotSupportedException(),
    };
  }
}