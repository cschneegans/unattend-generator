using System;
using System.Text;
using System.Xml;

namespace Schneegans.Unattend;

public interface IWifiSettings;

public class SkipWifiSettings : IWifiSettings;

public class InteractiveWifiSettings : IWifiSettings;

public enum WifiAuthentications
{
  Open, WPA2PSK, WPA3SAE
}

public record class UnattendedWifiSettings(
  string Name,
  string Password,
  bool ConnectAutomatically,
  WifiAuthentications Authentication,
  bool NonBroadcast
) : IWifiSettings;

public record class XmlWifiSettings(
  string Xml
) : IWifiSettings;

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
    else if (Configuration.WifiSettings is UnattendedWifiSettings settings)
    {
      elem.RemoveSelf();
      AddWifiProfile(settings);
    }
    else
    {
      throw new NotSupportedException();
    }
  }

  void AddWifiProfile(UnattendedWifiSettings settings)
  {
    string xmlfile = @"%TEMP%\wifi.xml";
    string logfile = @"%TEMP%\wifi.log";

    string xml = Util.ToPrettyString(GetWlanProfile(settings));
    Util.AddFile(xml, useCDataSection: true, xmlfile, Document, NamespaceManager);

    CommandAppender appender = new(Document, NamespaceManager, CommandConfig.Specialize);
    appender.Append(
      CommandBuilder.ShellCommand($@"netsh.exe wlan add profile filename=""{xmlfile}"" user=all", logfile)
    );
    appender.Append(
      CommandBuilder.ShellCommand($@"del ""{xmlfile}""")
    );

    if (settings.ConnectAutomatically)
    {
      appender.Append(
        CommandBuilder.ShellCommand($@"netsh.exe wlan connect name=""{settings.Name}"" ssid=""{settings.Name}""", logfile)
      );
    }
  }

  static XmlDocument GetWlanProfile(UnattendedWifiSettings settings)
  {
    var doc = Util.XmlDocumentFromResource("WLANProfile.xml");
    var ns = new XmlNamespaceManager(doc.NameTable);
    ns.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");

    {
      doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:name", ns).InnerText = settings.Name;
      doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:SSIDConfig/w:SSID/w:name", ns).InnerText = settings.Name;
      doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:SSIDConfig/w:SSID/w:hex", ns).InnerText = Convert.ToHexString(Encoding.UTF8.GetBytes(settings.Name));
    }
    {
      doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:connectionType", ns).InnerText = "ESS";
      doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:connectionMode", ns).InnerText = settings.ConnectAutomatically ? "auto" : "manual";
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
        doc.SelectSingleNodeOrThrow("/w:WLANProfile/w:MSM/w:security/w:sharedKey/w:keyMaterial", ns).InnerText = settings.Password;
      }

      void SetNonBroadcast()
      {
        if (settings.NonBroadcast)
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

      switch (settings.Authentication)
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
          throw
          new NotImplementedException($"Authentication mode {settings.Authentication} is not implemented.");
      }
    }

    Util.ValidateAgainstSchema(doc, "WLAN_profile_v1.xsd");

    return doc;
  }
}
