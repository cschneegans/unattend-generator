using System;
using System.Collections.Generic;
using System.Linq;

namespace Schneegans.Unattend;

public interface ILanguageSettings;

public class InteractiveLanguageSettings : ILanguageSettings;

public record class LocaleAndKeyboard(
  UserLocale Locale,
  KeyboardIdentifier Keyboard
);

public record class UnattendedLanguageSettings(
  ImageLanguage ImageLanguage,
  LocaleAndKeyboard LocaleAndKeyboard,
  LocaleAndKeyboard? LocaleAndKeyboard2,
  LocaleAndKeyboard? LocaleAndKeyboard3,
  GeoLocation GeoLocation
) : ILanguageSettings;

class LocalesModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    var componentPe = Document.SelectSingleNodeOrThrow("//u:component[@name = 'Microsoft-Windows-International-Core-WinPE']", NamespaceManager);
    var componentOobe = Document.SelectSingleNodeOrThrow("//u:component[@name = 'Microsoft-Windows-International-Core']", NamespaceManager);

    switch (Configuration.LanguageSettings)
    {
      case UnattendedLanguageSettings settings:
        {
          string keyboards =
            new List<LocaleAndKeyboard?>() {
              settings.LocaleAndKeyboard,
              settings.LocaleAndKeyboard2,
              settings.LocaleAndKeyboard3,
            }
            .SelectMany<LocaleAndKeyboard?, string>(pair =>
            {
              if (pair == null)
              {
                return [];
              }
              else if (pair.Keyboard.Type == InputType.IME)
              {
                return [pair.Keyboard.Id];
              }
              else if (pair.Locale.LCID == "1000")
              {
                UserLocale GetReplacementForUnspecifiedLocale()
                {
                  if (Generator.UserLocales.TryGetValue(settings.ImageLanguage.Id, out UserLocale? found))
                  {
                    return found;
                  }
                  else if (settings.ImageLanguage.Id == "zh-CN")
                  {
                    return Generator.UserLocales["zh"];
                  }
                  else if (settings.ImageLanguage.Id == "zh-TW")
                  {
                    return Generator.UserLocales["zh-Hant"];
                  }
                  else
                  {
                    return pair.Locale;
                  }
                }
                return [$"{GetReplacementForUnspecifiedLocale().LCID}:{pair.Keyboard.Id}"];
              }
              else
              {
                return [$"{pair.Locale.LCID}:{pair.Keyboard.Id}"];
              }
            })
            .JoinString(";");

          componentPe.SelectSingleNodeOrThrow("u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Id;
          componentOobe.SelectSingleNodeOrThrow("u:InputLocale", NamespaceManager).InnerText = keyboards;
          componentOobe.SelectSingleNodeOrThrow("u:SystemLocale", NamespaceManager).InnerText = settings.LocaleAndKeyboard.Locale.Id;
          componentOobe.SelectSingleNodeOrThrow("u:UserLocale", NamespaceManager).InnerText = settings.LocaleAndKeyboard.Locale.Id;
          componentOobe.SelectSingleNodeOrThrow("u:UILanguage", NamespaceManager).InnerText = settings.ImageLanguage.Id;
        }

        if (settings.GeoLocation.Id != settings.LocaleAndKeyboard.Locale.GeoLocation?.Id)
        {
          UserOnceScript.Append($"Set-WinHomeLocation -GeoId {settings.GeoLocation.Id};");
        }
        break;

      case InteractiveLanguageSettings:
        componentPe.RemoveSelf();
        componentOobe.RemoveSelf();
        break;

      default:
        throw new NotSupportedException();
    }
  }
}
