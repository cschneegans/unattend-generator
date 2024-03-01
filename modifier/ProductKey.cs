using System;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

public interface IEditionSettings;

public class InteractiveEditionSettings : IEditionSettings;

public record class UnattendedEditionSettings(
  WindowsEdition Edition
) : IEditionSettings;

public class DirectEditionSettings(
  string productKey
) : IEditionSettings
{
  public string ProductKey { get; } = Validate(productKey);

  private static string Validate(string key)
  {
    if (Regex.IsMatch(key, "^([A-Z0-9]{5}-){4}[A-Z0-9]{5}$"))
    {
      return key;
    }
    else
    {
      throw new ConfigurationException($"Product key {key} is ill-formed.");
    }
  }
}

class ProductKeyModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    Document.SelectSingleNodeOrThrow("//u:ProductKey/u:Key", NamespaceManager).InnerText = Configuration.EditionSettings switch
    {
      UnattendedEditionSettings settings => settings.Edition.ProductKey,
      DirectEditionSettings settings => settings.ProductKey,
      InteractiveEditionSettings => "00000-00000-00000-00000-00000",
      _ => throw new NotSupportedException()
    };
  }
}
