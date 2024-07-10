using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

public interface ILanguageSettings;

public class InteractiveLanguageSettings : ILanguageSettings;

public record class UnattendedLanguageSettings(
  ImageLanguage ImageLanguage,
  UserLocale UserLocale,
  KeyboardIdentifier InputLocale,
  KeyboardIdentifier? InputLocale2,
  KeyboardIdentifier? InputLocale3,
  GeoLocation GeoLocation
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
        string keyboards = string.Join(';',
          new List<KeyboardIdentifier?>() {
            settings.InputLocale,
            settings.InputLocale2,
            settings.InputLocale3,
          }
          .SelectMany<KeyboardIdentifier?, string>( k =>
          {
            return k == null ? ([]) : ([k.Id]);
          }
        ));

        XmlNode node = element.Node;
        node.SelectSingleNodeOrThrow("u:InputLocale", NamespaceManager).InnerText = keyboards;
        node.SelectSingleNodeOrThrow("u:SystemLocale", NamespaceManager).InnerText = settings.UserLocale.Id;
        node.SelectSingleNodeOrThrow("u:UserLocale", NamespaceManager).InnerText = settings.UserLocale.Id;
        node.SelectSingleNodeOrThrow("u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Id;
        if (element.Setup)
        {
          node.SelectSingleNodeOrThrow("u:SetupUILanguage/u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Id;
        }
      }

      if (settings.GeoLocation.Id != settings.UserLocale.GeoLocation?.Id)
      {
        CommandAppender appender = GetAppender(CommandConfig.Specialize);
        appender.Append(
          CommandBuilder.RegistryDefaultUserCommand((rootKey, subKey) =>
          {
            return [CommandBuilder.UserRunOnceCommand("GeoLocation", CommandBuilder.PowerShellCommand($"Set-WinHomeLocation -GeoId {settings.GeoLocation.Id};"), rootKey, subKey)];
          }
         ));
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
