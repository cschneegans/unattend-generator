using System.Xml;

namespace Schneegans.Unattend;

public interface ITimeZoneSettings;

public class ImplicitTimeZoneSettings : ITimeZoneSettings;

public record class ExplicitTimeZoneSettings(
  TimeOffset TimeZone
) : ITimeZoneSettings;

class TimeZoneModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.TimeZoneSettings is ExplicitTimeZoneSettings settings)
    {
      XmlElement component = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Shell-Setup", Document, NamespaceManager);
      NewSimpleElement("TimeZone", component, settings.TimeZone.Id);
    }
  }
}