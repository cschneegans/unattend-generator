namespace Schneegans.Unattend;

class ComponentsModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    foreach (var pair in Configuration.Components)
    {
      string component = pair.Key;
      foreach (var pass in pair.Value)
      {
        var setting = Document.SelectSingleNodeOrThrow($"/u:unattend/u:settings[@pass='{pass}']", NamespaceManager);
        if (setting.SelectSingleNode($"u:component[@name='{component}']", NamespaceManager) == null)
        {
          var elem = Document.CreateElement("component", NamespaceManager.LookupNamespace("u"));
          elem.SetAttribute("name", component);
          elem.SetAttribute("processorArchitecture", "x86");
          elem.SetAttribute("publicKeyToken", "31bf3856ad364e35");
          elem.SetAttribute("language", "neutral");
          elem.SetAttribute("versionScope", "nonSxS");
          elem.AppendChild(Document.CreateComment("Placeholder"));
          setting.AppendChild(elem);
        }
      }
    }
  }
}