using System;
using System.Collections.Generic;

namespace Schneegans.Unattend;

public enum ScriptType
{
  Cmd, Ps1
}
public enum ScriptPhase
{
  System, FirstLogon, UserOnce
}

public record class ScriptSettings(
  IEnumerable<Script> Scripts
);

public record class Script(
  string Content,
  ScriptPhase Phase,
  ScriptType Type
);

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
    string name = $"unattend-{++count:X2}";
    string extension = script.Type.ToString().ToLowerInvariant();
    return new ScriptId(@$"{ScriptsDirectory}\{name}.{extension}", name);
  }

  private void CreateScriptsDirectoryOnce()
  {
    if (!directoryCreated)
    {
      var appender = new CommandAppender(Document, NamespaceManager, CommandConfig.Specialize);
      appender.ShellCommand($"mkdir {ScriptsDirectory}");
      directoryCreated = true;
    }
  }

  private void WriteScriptContent(Script script, ScriptId scriptId)
  {
    var appender = new CommandAppender(Document, NamespaceManager, CommandConfig.Specialize);
    var lines = Util.SplitLines(script.Content);
    appender.WriteToFile(scriptId.FullName, lines);
  }

  private void CallScript(Script script, ScriptId scriptId)
  {
    var appender = new CommandAppender(Document, NamespaceManager, script.Phase switch
    {
      ScriptPhase.FirstLogon => CommandConfig.Oobe,
      _ => CommandConfig.Specialize,
    });

    switch (script.Phase)
    {
      case ScriptPhase.System:
      case ScriptPhase.FirstLogon:
        switch (script.Type)
        {
          case ScriptType.Cmd:
            appender.Command(scriptId.FullName);
            break;
          case ScriptType.Ps1:
            appender.InvokePowerShellScript(scriptId.FullName);
            break;
          default:
            throw new NotSupportedException();
        }
        break;
      case ScriptPhase.UserOnce:
        appender.RegistryDefaultUserCommand((rootKey, subKey) =>
        {
          string command = script.Type switch
          {
            ScriptType.Cmd => scriptId.FullName,
            ScriptType.Ps1 => appender.GetPowerShellCommand($"Get-Content -LiteralPath '{scriptId.FullName}' -Raw | Invoke-Expression;"),
            _ => throw new NotSupportedException(),
          };
          appender.UserRunOnceCommand(scriptId.Key, command, rootKey, subKey);
        });
        break;
      default:
        throw new NotSupportedException();
    }
  }
}