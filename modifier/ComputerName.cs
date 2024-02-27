using System.Diagnostics.CodeAnalysis;
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

class ComputerNameModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.ComputerNameSettings is CustomComputerNameSettings settings)
    {
      XmlElement component = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Shell-Setup", Document, NamespaceManager);
      NewSimpleElement("ComputerName", component, settings.ComputerName);
    }
  }
}
