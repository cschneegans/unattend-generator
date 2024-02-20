using System;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

class ProcessorArchitectureModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    var archs = Configuration.ProcessorArchitectures.ToList().ConvertAll(pa => pa.ToString());
    if (archs.Count == 0)
    {
      throw new Exception("Must specify at least one processor architecture.");
    }

    foreach (XmlElement component in Document.SelectNodesOrEmpty("//*[@processorArchitecture]").Cast<XmlElement>())
    {
      component.SetAttribute("processorArchitecture", archs[0]);
      for (int i = 1; i < archs.Count; i++)
      {
        var copy = component.CloneNode(true) as XmlElement;
        copy.SetAttribute("processorArchitecture", archs[i]);
        component.ParentNode.InsertAfter(copy, component);
      }
    }
  }
}
