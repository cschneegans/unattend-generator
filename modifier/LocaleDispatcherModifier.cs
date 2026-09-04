using System;
using System.Linq;
using System.Reflection;

namespace Schneegans.Unattend;

/// <summary>
/// 選択されたロケール/キーボードに応じて適切な LocaleSpecificModifier を自動呼出するディスパッチャー
/// </summary>
class LocaleDispatcherModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.LanguageSettings is not UnattendedLanguageSettings settings)
    {
      return;
    }

    // アセンブリ内から LocaleSpecificModifier を継承した具象クラスを自動検出
    var modifierTypes = Assembly.GetExecutingAssembly().GetTypes()
      .Where(t => !t.IsAbstract && typeof(LocaleSpecificModifier).IsAssignableFrom(t));

    foreach (var type in modifierTypes)
    {
      try
      {
        // インスタンス化して適用条件を判定
        var instance = Activator.CreateInstance(
          type,
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
          binder: null,
          args: [context],
          culture: null
        );

        if (instance is LocaleSpecificModifier modifier && modifier.IsApplicable(settings))
        {
          modifier.Process();
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error executing locale modifier {type.Name}: {ex.Message}");
      }
    }
  }
}

