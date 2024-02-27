namespace Schneegans.Unattend;

class ExpressSettingsModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    var elem = Document.SelectSingleNodeOrThrow("//u:OOBE/u:ProtectYourPC", NamespaceManager);

    switch (Configuration.ExpressSettings)
    {
      case ExpressSettingsMode.Interactive:
        elem.RemoveSelf();
        break;
      case ExpressSettingsMode.EnableAll:
        elem.InnerText = "1";
        break;
      case ExpressSettingsMode.DisableAll:
        elem.InnerText = "3";
        break;
    }
  }
}
