using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

public interface IComputerNameSettings;

public class RandomComputerNameSettings : IComputerNameSettings;

public record class CustomComputerNameSettings(string? ComputerName) : IComputerNameSettings
{
  public string ComputerName { get; } = Validate(ComputerName);

  static string Validate(string? name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      Throw();
    }

    if (name.Length > 15)
    {
      Throw();
    }

    if (name.ToCharArray().Any(char.IsWhiteSpace))
    {
      Throw();
    }

    if (name.ToCharArray().All(char.IsAsciiDigit))
    {
      Throw();
    }

    if (name.IndexOfAny(['{', '|', '}', '~', '[', '\\', ']', '^', '\'', ':', ';', '<', '=', '>', '?', '@', '!', '"', '#', '$', '%', '`', '(', ')', '+', '/', '.', ',', '*', '&']) > -1)
    {
      Throw();
    }

    [DoesNotReturn]
    void Throw()
    {
      throw new ConfigurationException($"Computer name '{name}' is invalid.");
    }

    return name;
  }
}

public record class ScriptComputerNameSettings(
  string Script
) : IComputerNameSettings;

class ComputerNameModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    void SetComputerName(string value)
    {
      XmlElement component = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Shell-Setup", Document, NamespaceManager);
      NewSimpleElement("ComputerName", component, value);
    }

    switch (Configuration.ComputerNameSettings)
    {
      case CustomComputerNameSettings settings:
        SetComputerName(settings.ComputerName);
        break;

      case ScriptComputerNameSettings settings:
        SetComputerName("TEMPNAME");
        string getterFile = EmbedTextFile("GetComputerName.ps1", settings.Script);
        string setterFile = EmbedTextFileFromResource("SetComputerName.ps1");
        SpecializeScript.Append($"""
            & '{getterFile}' > 'C:\Windows\Setup\Scripts\ComputerName.txt';
            Start-Process -FilePath ( Get-Process -Id $PID ).Path -ArgumentList '-ExecutionPolicy "Unrestricted" -NoProfile -File "{setterFile}"' -WindowStyle 'Hidden';
            Start-Sleep -Seconds 10;
            """);
        break;

      case RandomComputerNameSettings:
        break;

      default:
        throw new NotSupportedException();
    }
  }
}
