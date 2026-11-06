using System;
using System.Collections.Generic;
using System.Linq;

namespace Schneegans.Unattend;

/// <summary>
/// Attribute declaring target locales and keyboards.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class TargetLocaleAttribute(string localeId) : Attribute
{
  public string LocaleId { get; } = localeId;
  public string[] KeyboardIds { get; set; } = [];
}

/// <summary>
/// Base class for locale-specific modifiers.
/// </summary>
abstract class LocaleSpecificModifier(ModifierContext context) : Modifier(context)
{
  /// <summary>
  /// Determines whether this modifier should be applied to the current configuration (language/keyboard settings).
  /// By default, automatically determines based on <see cref="TargetLocaleAttribute"/> attributes.
  /// </summary>
  public virtual bool IsApplicable(UnattendedLanguageSettings settings)
  {
    var attrs = (TargetLocaleAttribute[])GetType().GetCustomAttributes(typeof(TargetLocaleAttribute), inherit: false);
    if (attrs.Length == 0)
    {
      return false;
    }

    var locales = new List<string> { settings.LocaleAndKeyboard.Locale.Id, settings.ImageLanguage.Id };
    if (settings.LocaleAndKeyboard2 != null)
    {
      locales.Add(settings.LocaleAndKeyboard2.Locale.Id);
    }
    if (settings.LocaleAndKeyboard3 != null)
    {
      locales.Add(settings.LocaleAndKeyboard3.Locale.Id);
    }

    var keyboardIds = new List<string> { settings.LocaleAndKeyboard.Keyboard.Id };
    if (settings.LocaleAndKeyboard2 != null)
    {
      keyboardIds.Add(settings.LocaleAndKeyboard2.Keyboard.Id);
    }
    if (settings.LocaleAndKeyboard3 != null)
    {
      keyboardIds.Add(settings.LocaleAndKeyboard3.Keyboard.Id);
    }

    foreach (var attr in attrs)
    {
      // Check for language/locale match
      if (locales.Any(l => attr.LocaleId.Equals(l, StringComparison.OrdinalIgnoreCase)))
      {
        return true;
      }

      // Check for keyboard ID match
      if (attr.KeyboardIds.Any(k =>
          keyboardIds.Any(kid => kid.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                                 kid.StartsWith(k + ":", StringComparison.OrdinalIgnoreCase))))
      {
        return true;
      }
    }

    return false;
  }
}

