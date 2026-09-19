using System;
using System.Collections.Generic;
using System.Linq;

namespace Schneegans.Unattend;

/// <summary>
/// 対象とするロケールやキーボードを宣言する属性
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class TargetLocaleAttribute(string localeId) : Attribute
{
  public string LocaleId { get; } = localeId;
  public string[] KeyboardIds { get; set; } = [];
}

/// <summary>
/// ロケール特化 Modifier の基底クラス
/// </summary>
abstract class LocaleSpecificModifier(ModifierContext context) : Modifier(context)
{
  /// <summary>
  /// 現在の構成（言語・キーボード設定）に対して本 Modifier を適用すべきかを判定する。
  /// 既定では TargetLocale 属性に基づいて自動判定する。
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
      // 言語/ロケールの一致確認
      if (locales.Any(l => attr.LocaleId.Equals(l, StringComparison.OrdinalIgnoreCase)))
      {
        return true;
      }

      // キーボードIDの一致確認
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

