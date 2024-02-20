using System;

namespace Schneegans.Unattend;

class ProductKeyModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    Document.SelectSingleNodeOrThrow("//u:ProductKey/u:Key", NamespaceManager).InnerText = Configuration.EditionSettings switch
    {
      UnattendedEditionSettings settings => settings.Edition.ProductKey,
      InteractiveEditionSettings => "00000-00000-00000-00000-00000",
      _ => throw new NotSupportedException()
    };
  }
}
