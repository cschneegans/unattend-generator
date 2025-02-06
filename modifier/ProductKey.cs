using System;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

public interface IEditionSettings;

public class InteractiveEditionSettings : IEditionSettings;

public record class FirmwareEditionSettings : IEditionSettings;

public record class UnattendedEditionSettings(
  WindowsEdition Edition
) : IEditionSettings;

public class CustomEditionSettings(
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
    const string zero = "00000-00000-00000-00000-00000";
    (string key, string ui) = Configuration.EditionSettings switch
    {
      UnattendedEditionSettings settings => (settings.Edition.ProductKey, "OnError"),
      CustomEditionSettings settings => (settings.ProductKey, "OnError"),
      InteractiveEditionSettings => (zero, "Always"),
      FirmwareEditionSettings => (zero, "OnError"),
      _ => throw new NotSupportedException()
    };

    Document.SelectSingleNodeOrThrow("//u:ProductKey/u:Key", NamespaceManager).InnerText = key;
    Document.SelectSingleNodeOrThrow("//u:ProductKey/u:WillShowUI", NamespaceManager).InnerText = ui;
  }
}
