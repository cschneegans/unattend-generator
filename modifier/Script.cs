using System;
using System.Collections.Generic;
using System.IO;

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

  public override void Process()
  {
    foreach (Script script in Configuration.ScriptSettings.Scripts)
    {
      if (!string.IsNullOrWhiteSpace(script.Content))
      {
        string filepath = GetScriptPath(script);
        WriteScriptContent(script, filepath);
        CallScript(script, filepath);
      }
    }
  }

  private string GetScriptPath(Script script)
  {
    string filename = $"unattend-{++count:X2}";
    string extension = script.Type.ToString().ToLowerInvariant();
    return @$"C:\Windows\Setup\Scripts\{filename}.{extension}";
  }

  private void WriteScriptContent(Script script, string filepath)
  {
    var appender = new CommandAppender(Document, NamespaceManager, CommandConfig.Specialize);
    var lines = Util.SplitLines(script.Content);
    appender.WriteToFile(filepath, lines);
  }

  private void CallScript(Script script, string filepath)
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
            appender.Command(filepath);
            break;
          case ScriptType.Ps1:
            appender.InvokePowerShellScript(filepath);
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
            ScriptType.Cmd => filepath,
            ScriptType.Ps1 => appender.GetPowerShellCommand($"Get-Content -LiteralPath '{filepath}' -Raw | Invoke-Expression;"),
            _ => throw new NotSupportedException(),
          };
          appender.UserRunOnceCommand(Path.GetFileNameWithoutExtension(filepath), command, rootKey, subKey);
        });
        break;
      default:
        throw new NotSupportedException();
    }
  }
}