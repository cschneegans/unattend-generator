using System;
using System.Linq;
using System.Reflection;

namespace Schneegans.Unattend;

/// <summary>
/// Dispatcher that automatically invokes appropriate <see cref="LocaleSpecificModifier"/> instances based on the selected locale/keyboard.
/// </summary>
class LocaleDispatcherModifier : Modifier
{
  private readonly ModifierContext context;

  public LocaleDispatcherModifier(ModifierContext context) : base(context)
  {
    this.context = context;
  }

  public override void Process()
  {
    if (Configuration.LanguageSettings is not UnattendedLanguageSettings settings)
    {
      return;
    }

    // Automatically discover concrete classes in the assembly that inherit from LocaleSpecificModifier
    var modifierTypes = Assembly.GetExecutingAssembly().GetTypes()
      .Where(t => !t.IsAbstract && typeof(LocaleSpecificModifier).IsAssignableFrom(t));

    foreach (var type in modifierTypes)
    {
      try
      {
        // Instantiate and determine applicability
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

