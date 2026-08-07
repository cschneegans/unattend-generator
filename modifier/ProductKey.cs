using System;
using System.Text.RegularExpressions;
using System.Xml;

namespace Schneegans.Unattend;

public interface IEditionSettings;

public class InteractiveEditionSettings : IEditionSettings;

public record class FirmwareEditionSettings : IEditionSettings;

public record class UnattendedEditionSettings(
  WindowsEdition Edition
) : IEditionSettings;

public record class ProductKey(string Value)
{
  public string Value { get; } = Initialize(Value);

  private static string Initialize(string value)
  {
    if (Regex.IsMatch(value, "^([A-Z0-9]{5}-){4}[A-Z0-9]{5}$", RegexOptions.IgnoreCase))
    {
      return value.ToUpperInvariant();
    }
    else
    {
      throw new ConfigurationException($"Product key '{value}' is ill-formed.");
    }
  }

  public override string ToString()
  {
    return Value;
  }
}

public record class CustomEditionSettings(
  ProductKey ProductKey
) : IEditionSettings;

class ProductKeyModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    {
      XmlNode keyElement = Document.SelectSingleNodeOrThrow("//u:UserData/u:ProductKey/u:Key", NamespaceManager);
      XmlNode uiElement = Document.SelectSingleNodeOrThrow("//u:UserData/u:ProductKey/u:WillShowUI", NamespaceManager);

      void SetWithoutKey(string ui)
      {
        keyElement.RemoveSelf();
        uiElement.InnerText = ui;
      }

      void Set(string key, string ui)
      {
        keyElement.InnerText = key;
        uiElement.InnerText = ui;
      }

      if (Configuration.PESettings is DefaultPESettings dps)
      {
        switch (dps.EditionSettings)
        {
          case UnattendedEditionSettings settings:
            Set(settings.Edition.ProductKey, "OnError");
            break;
          case CustomEditionSettings settings:
            Set(settings.ProductKey.Value, "OnError");
            break;
          case InteractiveEditionSettings:
            Set("00000-00000-00000-00000-00000", "Always");
            break;
          case FirmwareEditionSettings:
            SetWithoutKey("Never");
            break;
          default:
            throw new NotSupportedException();
        }
      }
    }
    {
      ProductKey? GetKeyForActivation()
      {
        if (Configuration.ActivationKey != null)
        {
          return Configuration.ActivationKey;
        }
        if (Configuration.PESettings is DefaultPESettings dps && dps.EditionSettings is CustomEditionSettings ces)
        {
          return ces.ProductKey;
        }
        return null;
      }
      if (GetKeyForActivation() is ProductKey key)
      {
        var elem = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Shell-Setup", "ProductKey", Document, NamespaceManager);
        elem.InnerText = key.Value;
      }
    }
  }
}
