using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

class ProcessorArchitectureModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    foreach (XmlElement component in Document.SelectNodesOrEmpty("//*[@processorArchitecture]").Cast<XmlElement>())
    {
      var archs = Configuration.ProcessorArchitectures.GetEnumerator();
      if (!archs.MoveNext())
      {
        throw new ConfigurationException("At least one processor architecture must be selected.");
      }

      void SetAttribute(XmlElement element)
      {
        element.SetAttribute("processorArchitecture", archs.Current.ToString());
      }

      SetAttribute(component);
      while (archs.MoveNext())
      {
        var copy = (XmlElement)component.CloneNode(true);
        SetAttribute(copy);
        component.ParentNode.InsertAfter(copy, component);
      }
    }
  }
}
