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

      switch (Configuration.EditionSettings)
      {
        case UnattendedEditionSettings settings:
          Set(settings.Edition.ProductKey, "OnError");
          break;
        case CustomEditionSettings settings:
          Set(settings.ProductKey, "OnError");
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
    {
      if (Configuration.EditionSettings is CustomEditionSettings settings)
      {
        var elem = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Shell-Setup", "ProductKey", Document, NamespaceManager);
        elem.InnerText = settings.ProductKey;
      }
    }
  }
}
