using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

class PrettyModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    Document
      .SelectNodesOrEmpty("//*")
      .Cast<XmlElement>()
      .ToList()
      .ForEach(node => node.IsEmpty = false);

    Document.Normalize();

    Document
      .SelectNodesOrEmpty("//text()")
      .Where(node => node is XmlWhitespace)
      .ToList()
      .ForEach(node => node.RemoveSelf());
  }
}
