using System;
using System.Xml;
using System.Xml.Schema;

namespace Schneegans.Unattend;

public interface IAppLockerSettings;

public class SkipAppLockerSettings : IAppLockerSettings;

public record class ConfigureAppLockerSettings(
  string PolicyXml
) : IAppLockerSettings;

class AppLockerModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    if (Configuration.AppLockerSettings is SkipAppLockerSettings)
    {
      return;
    }
    else if (Configuration.AppLockerSettings is ConfigureAppLockerSettings settings)
    {
      XmlDocument policy = new();
      try
      {
        policy.LoadXml(settings.PolicyXml);
        Util.ValidateAgainstSchema(policy, "AppLocker.xsd");
      }
      catch (Exception e) when (e is XmlException or XmlSchemaException)
      {
        throw new ConfigurationException($"AppLocker policy XML is invalid: {e.Message}");
      }

      string xmlFile = EmbedXmlFile("AppLockerPolicy.xml", policy);
      SpecializeScript.Append($"""
        Get-Service -Name 'AppIDSvc' | Set-Service -StartupType 'Automatic';
        Get-Service -Name 'AppIDSvc' | Start-Service;
        Set-AppLockerPolicy -XmlPolicy '{xmlFile}';
        """);
    }
    else
    {
      throw new NotSupportedException();
    }
  }
}