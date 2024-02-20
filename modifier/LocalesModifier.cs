using System;
using System.Xml;

namespace Schneegans.Unattend;

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
        node.SelectSingleNodeOrThrow("u:InputLocale", NamespaceManager).InnerText = settings.InputLocale.Code;
        node.SelectSingleNodeOrThrow("u:SystemLocale", NamespaceManager).InnerText = settings.UserLocale.Code;
        node.SelectSingleNodeOrThrow("u:UserLocale", NamespaceManager).InnerText = settings.UserLocale.Code;
        node.SelectSingleNodeOrThrow("u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Tag;
        if (element.Setup)
        {
          node.SelectSingleNodeOrThrow("u:SetupUILanguage/u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Tag;
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
