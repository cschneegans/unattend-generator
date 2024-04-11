using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;

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
      ScriptPhase.DefaultUser => [ScriptType.Reg],
      _ => Enum.GetValues<ScriptType>(),
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

    if (phase == ScriptPhase.DefaultUser && type == ScriptType.Reg && !string.IsNullOrWhiteSpace(content))
    {
      string prefix = @"[HKEY_USERS\DefaultUser\";
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

class ScriptModifier(ModifierContext context) : Modifier(context)
{
  private int count = 0;

  public override void Process()
  {
    var scriptsMap = Configuration.ScriptSettings.Scripts.ToImmutableDictionary(NewScriptId);
    if (scriptsMap.IsEmpty)
    {
      return;
    }
    foreach (var pair in scriptsMap)
    {
      WriteScriptContent(pair.Value, pair.Key);
    }
    {
      const string psPath = @"C:\Windows\Temp\ExtractScripts.ps1";
      CommandAppender appender = new(Document, NamespaceManager, new SpecializeCommandConfig());
      appender.Append([
        .. CommandBuilder.WriteToFile(psPath, Util.SplitLines(Util.StringFromResource("ExtractScripts.ps1"))),
        CommandBuilder.InvokePowerShellScript(psPath)
      ]);
    }
    foreach (var pair in scriptsMap)
    {
      CallScript(pair.Value, pair.Key);
    }
  }

  record class ScriptId(string FullName, string Key);

  private ScriptId NewScriptId(Script script)
  {
    string name = $"unattend-{++count:x2}";
    string extension = script.Type.ToString().ToLowerInvariant();
    return new ScriptId(@$"C:\Windows\Setup\Scripts\{name}.{extension}", name);
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

    {
      XmlNode root = Document.SelectSingleNodeOrThrow("/u:unattend", NamespaceManager);
      XmlNode? extensions = root.SelectSingleNode("s:Extensions", NamespaceManager);
      if (extensions == null)
      {
        extensions = Document.CreateElement("Extensions", Constants.MyNamespaceUri);
        root.AppendChild(extensions);
      }

      XmlElement file = Document.CreateElement("File", Constants.MyNamespaceUri);
      file.SetAttribute("path", scriptId.FullName);
      file.InnerText = Clean(script);
      extensions.AppendChild(file);
    }
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