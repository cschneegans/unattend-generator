using System.Xml;

namespace Schneegans.Unattend;

class EmptyElementsModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    bool modified;
    do
    {
      modified = false;
      var leaves = Document.SelectNodesOrEmpty("//*");
      foreach (XmlNode leaf in leaves)
      {
        if (Drop(leaf))
        {
          leaf.RemoveSelf();
          modified = true;
        }
      }
    } while (modified);
  }

  private static bool Drop(XmlNode leaf)
  {
    if (leaf.HasChildNodes)
    {
      return false;
    }

    if (leaf.Attributes != null && leaf.Attributes.Count > 0)
    {
      return false;
    }

    return true;
  }
}
