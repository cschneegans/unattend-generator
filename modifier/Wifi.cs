using System;
using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace Schneegans.Unattend;

public interface IWifiSettings;

public class SkipWifiSettings : IWifiSettings;

public class InteractiveWifiSettings : IWifiSettings;

public enum WifiAuthentications
{
  Open, WPA2PSK, WPA3SAE
}

public interface IProfileWifiSettings : IWifiSettings
{
  XmlDocument ProfileXml { get; }

  bool ConnectAutomatically { get; }

  string Name { get; }

  string Ssid { get; }
}

public class XmlWifiSettings : IProfileWifiSettings
{
  private readonly XmlDocument doc;
  private readonly XmlNamespaceManager ns;

  public XmlWifiSettings(string Xml)
  {
    doc = new XmlDocument();
    ns = new XmlNamespaceManager(doc.NameTable);
    ns.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");
    doc.LoadXml(Xml);
  }

  public bool ConnectAutomatically
  {
    get
    {
      XmlNode? elem = doc.SelectSingleNode("/w:WLANProfile/w:connectionMode", ns);
      if (elem == null)
      {
        return false;
      }
      return elem.InnerText == "auto";
    }
  }

  public string Name
  {
    get
    {
      return doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:name", ns).InnerText;
    }
  }

  public string Ssid
  {
    get
    {
      XmlNode? hex = doc.SelectSingleNode("/w:WLANProfile/w:SSIDConfig/w:SSID/w:hex", ns);
      if (hex != null)
      {
        return Encoding.UTF8.GetString(Convert.FromHexString(hex.InnerText));
      }
      else
      {
        return doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:SSIDConfig/w:SSID/w:name", ns).InnerText;
      }
    }
  }

  public XmlDocument ProfileXml => doc;
}

public record class ParameterizedWifiSettings(
    string Name,
    string Password,
    bool ConnectAutomatically,
    WifiAuthentications Authentication,
    bool NonBroadcast
) : IProfileWifiSettings
{
  public string Ssid => Name;

  public XmlDocument ProfileXml
  {
    get
    {
      var doc = Util.XmlDocumentFromResource("WLANProfile.xml");
      var ns = new XmlNamespaceManager(doc.NameTable);
      ns.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");

      {
        doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:name", ns).InnerText = Name;
        doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:SSIDConfig/w:SSID/w:name", ns).InnerText = Name;
        doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:SSIDConfig/w:SSID/w:hex", ns).InnerText = Convert.ToHexString(Encoding.UTF8.GetBytes(Name));
      }
      {
        doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:connectionType", ns).InnerText = "ESS";
        doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:connectionMode", ns).InnerText = ConnectAutomatically ? "auto" : "manual";
      }
      {
        void SetAuthentication(string value)
        {
          doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:MSM/w:security/w:authEncryption/w:authentication", ns).InnerText = value;
        }

        void SetEncryption(string value)
        {
          doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:MSM/w:security/w:authEncryption/w:encryption", ns).InnerText = value;
        }

        void SetPassword()
        {
          doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:MSM/w:security/w:sharedKey/w:keyMaterial", ns).InnerText = Password;
        }

        void SetNonBroadcast()
        {
          if (NonBroadcast)
          {
            XmlElement elem = doc.CreateElement("nonBroadcast", ns.LookupNamespace("w"));
            elem.InnerText = "true";
            doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:SSIDConfig", ns).AppendChild(elem);
          }
        }

        void SetTransitionMode()
        {
          XmlElement elem = doc.CreateElement("transitionMode", "http://www.microsoft.com/networking/WLAN/profile/v4");
          elem.InnerText = "true";
          doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:MSM/w:security/w:authEncryption", ns).AppendChild(elem);
        }

        switch (Authentication)
        {
          case WifiAuthentications.Open:
            {
              SetAuthentication("open");
              SetEncryption("none");
              SetNonBroadcast();
              doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:MSM/w:security/w:sharedKey", ns).RemoveSelf();
              break;
            }
          case WifiAuthentications.WPA2PSK:
            {
              SetAuthentication("WPA2PSK");
              SetEncryption("AES");
              SetNonBroadcast();
              SetPassword();
              break;
            }
          case WifiAuthentications.WPA3SAE:
            {
              SetAuthentication("WPA3SAE");
              SetEncryption("AES");
              SetNonBroadcast();
              SetPassword();
              SetTransitionMode();
              break;
            }
          default:
            throw new NotImplementedException($"Authentication mode {Authentication} is not implemented.");
        }
      }

      return doc;
    }
  }
}

class WifiModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    XmlNode elem = Document.SelectSingleNodeOrThrow("//u:OOBE/u:HideWirelessSetupInOOBE", NamespaceManager);

    if (Configuration.WifiSettings is InteractiveWifiSettings)
    {
      elem.InnerText = "false";
    }
    else if (Configuration.WifiSettings is SkipWifiSettings)
    {
      elem.InnerText = "true";
    }
    else if (Configuration.WifiSettings is IProfileWifiSettings settings)
    {
      elem.RemoveSelf();
      AddWifiProfile(settings);
    }
    else
    {
      throw new NotSupportedException();
    }
  }

  void AddWifiProfile(IProfileWifiSettings settings)
  {
    string xmlfile = @"%TEMP%\wifi.xml";
    string logfile = @"%TEMP%\wifi.log";

    XmlDocument profile = settings.ProfileXml;
    try
    {
      Util.ValidateAgainstSchema(profile, "WLAN_profile_v1.xsd");
    }
    catch (Exception e) when (e is XmlException or XmlSchemaException)
    {
      throw new ConfigurationException($"WLAN profile XML is invalid: {e.Message}");
    }

    AddXmlFile(profile, xmlfile);

    CommandAppender appender = GetAppender(CommandConfig.Specialize);
    appender.Append([
      CommandBuilder.ShellCommand($@"netsh.exe wlan add profile filename=""{xmlfile}"" user=all", logfile),
      CommandBuilder.ShellCommand($@"del ""{xmlfile}"""),
    ]);

    if (settings.ConnectAutomatically)
    {
      appender.Append(
        CommandBuilder.ShellCommand($@"netsh.exe wlan connect name=""{settings.Name}"" ssid=""{settings.Name}""", logfile)
      );
    }
  }
}
