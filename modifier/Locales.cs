using System;
using System.Xml;

namespace Schneegans.Unattend;

public interface ILanguageSettings;

public class InteractiveLanguageSettings : ILanguageSettings;

public record class UnattendedLanguageSettings(
  ImageLanguage ImageLanguage,
  UserLocale UserLocale,
  KeyboardIdentifier InputLocale
) : ILanguageSettings;

class LocalesModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    var elements = new[] {
      new {
        Node = Document.SelectSingleNodeOrThrow("//u:component[@name = 'Microsoft-Windows-International-Core-WinPE']", NamespaceManager),
        Setup = true
      },
      new {
        Node = Document.SelectSingleNodeOrThrow("//u:component[@name = 'Microsoft-Windows-International-Core']", NamespaceManager),
        Setup = false
      }
    };

    if (Configuration.LanguageSettings is UnattendedLanguageSettings settings)
    {
      foreach (var element in elements)
      {
        XmlNode node = element.Node;
        node.SelectSingleNodeOrThrow("u:InputLocale", NamespaceManager).InnerText = settings.InputLocale.Id;
        node.SelectSingleNodeOrThrow("u:SystemLocale", NamespaceManager).InnerText = settings.UserLocale.Id;
        node.SelectSingleNodeOrThrow("u:UserLocale", NamespaceManager).InnerText = settings.UserLocale.Id;
        node.SelectSingleNodeOrThrow("u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Id;
        if (element.Setup)
        {
          node.SelectSingleNodeOrThrow("u:SetupUILanguage/u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Id;
        }
      }
    }
    else if (Configuration.LanguageSettings is InteractiveLanguageSettings)
    {
      foreach (var element in elements)
      {
        element.Node.RemoveSelf();
      }
    }
    else
    {
      throw new NotSupportedException();
    }
  }
}
