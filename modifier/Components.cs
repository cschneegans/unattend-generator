using System.Linq;
using System.Xml;

namespace Schneegans.Unattend;

class ComponentsModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    foreach (var item in Configuration.Components)
    {
      var setting = Document.SelectSingleNodeOrThrow($"/u:unattend/u:settings[@pass='{item.Key.Pass}']", NamespaceManager);
      var component = (XmlElement?)setting.SelectSingleNode($"u:component[@name='{item.Key.Component}']", NamespaceManager);

      if (component == null)
      {
        component = Document.CreateElement("component", NamespaceManager.LookupNamespace("u"));
        component.SetAttribute("name", item.Key.Component);
        component.SetAttribute("processorArchitecture", "x86");
        component.SetAttribute("publicKeyToken", "31bf3856ad364e35");
        component.SetAttribute("language", "neutral");
        component.SetAttribute("versionScope", "nonSxS");
      }
      else
      {
        component.InnerXml = "";
      }

      var newDoc = new XmlDocument();
      try
      {
        newDoc.LoadXml($"<root xmlns='urn:schemas-microsoft-com:unattend' xmlns:wcm='http://schemas.microsoft.com/WMIConfig/2002/State'>{item.Value}</root>");
      }
      catch (XmlException)
      {
        throw new ConfigurationException($"Your XML markup '{item.Value}' is not well-formed.");
      }
      if (newDoc.DocumentElement!.SelectNodesOrEmpty("//*[local-name()='settings' or local-name()='component']").Any())
      {
        throw new ConfigurationException($"You must not include elements 'settings' or 'component' with your XML markup '{item.Value}'.");
      }
      foreach (XmlNode node in newDoc.DocumentElement!.ChildNodes)
      {
        component.AppendChild(Document.ImportNode(node, deep: true));
      }
      setting.AppendChild(component);
    }
  }
}
