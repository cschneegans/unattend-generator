using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

public interface IComputerNameSettings;

public class RandomComputerNameSettings : IComputerNameSettings;

public class CustomComputerNameSettings : IComputerNameSettings
{
  public CustomComputerNameSettings(string? name)
  {
    ComputerName = Validate(name);
  }

  public string ComputerName { get; }

  private string Validate(string? name)
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

    return name;
  }

  [DoesNotReturn]
  private void Throw()
  {
    throw new ConfigurationException($"Computer name '{ComputerName}' is invalid.");
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
        CommandAppender appender = GetAppender(CommandConfig.Specialize);
        string resultFile = @"C:\Windows\Setup\Scripts\ComputerName.txt";
        {
          string getterFile = @"C:\Windows\Setup\Scripts\GetComputerName.ps1";
          AddTextFile(settings.Script, getterFile);
          appender.Append(
            CommandBuilder.PowerShellCommand(@$"& {{ Get-Content -LiteralPath '{getterFile}' -Raw | Invoke-Expression > {resultFile}; }} *>&1 >> 'C:\Windows\Setup\Scripts\GetComputerName.log';")
          );
        }
        {
          string setterFile = @"C:\Windows\Setup\Scripts\SetComputerName.ps1";
          string script = Util.StringFromResource("SetComputerName.ps1");

          StringWriter writer = new();
          writer.WriteLine($"$newName = Get-Content -LiteralPath '{resultFile}' -Raw;");
          writer.WriteLine(script);
          AddTextFile(writer.ToString(), setterFile);
          appender.Append(
            CommandBuilder.Raw($@"cmd.exe /c start /MIN {CommandBuilder.InvokePowerShellScript(setterFile)}")
          );
        }

        break;

      case RandomComputerNameSettings:
        break;

      default:
        throw new NotSupportedException();
    }
  }
}
