using System;
using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

class OrderModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    foreach (XmlElement container in Document.SelectNodesOrEmpty("//u:RunSynchronous | //u:RunAsynchronous | //u:FirstLogonCommands", NamespaceManager).Cast<XmlElement>())
    {
      int pos = 1;
      foreach (XmlElement child in container.SelectNodesOrEmpty("*").Cast<XmlElement>())
      {
        if (child.SelectNodesOrEmpty("u:Order", NamespaceManager).Any())
        {
          throw new Exception($"'{child.OuterXml}' already contains an <Order> element.");
        }

        var order = Document.CreateElement("Order", NamespaceManager.LookupNamespace("u"));
        order.InnerText = $"{pos++}";
        child.PrependChild(order);
        child.SetAttribute("action", NamespaceManager.LookupNamespace("wcm"), "add");
      }
    }
  }
}
