using System;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

public interface IEditionSettings;

public class InteractiveEditionSettings : IEditionSettings;

public record class FirmwareEditionSettings : IEditionSettings;

public record class UnattendedEditionSettings(
  WindowsEdition Edition
) : IEditionSettings;

public class CustomEditionSettings(
  string productKey
) : IEditionSettings
{
  public string ProductKey { get; } = Validate(productKey);

  private static string Validate(string key)
  {
    if (Regex.IsMatch(key, "^([A-Z0-9]{5}-){4}[A-Z0-9]{5}$"))
    {
      return key;
    }
    else
    {
      throw new ConfigurationException($"Product key {key} is ill-formed.");
    }
  }
}

public interface IInstallFromSettings;

public class AutomaticInstallFromSettings : IInstallFromSettings;

public abstract record class KeyInstallFromSettings(
  string Key,
  string Value
) : IInstallFromSettings;

public record class IndexInstallFromSettings(
  int Index
) : KeyInstallFromSettings("/IMAGE/INDEX", Index.ToString());

public record class NameInstallFromSettings(
  string Name
) : KeyInstallFromSettings("/IMAGE/NAME", Name);

public record class DescriptionInstallFromSettings(
  string Description
) : KeyInstallFromSettings("/IMAGE/DESCRIPTION", Description);

class ProductKeyModifier(ModifierContext context) : Modifier(context)
{
  public override void Process()
  {
    {
      const string zero = "00000-00000-00000-00000-00000";
      (string key, string ui) = Configuration.EditionSettings switch
      {
        UnattendedEditionSettings settings => (settings.Edition.ProductKey, "OnError"),
        CustomEditionSettings settings => (settings.ProductKey, "OnError"),
        InteractiveEditionSettings => (zero, "Always"),
        FirmwareEditionSettings => (zero, "OnError"),
        _ => throw new NotSupportedException()
      };

      Document.SelectSingleNodeOrThrow("//u:ProductKey/u:Key", NamespaceManager).InnerText = key;
      Document.SelectSingleNodeOrThrow("//u:ProductKey/u:WillShowUI", NamespaceManager).InnerText = ui;
    }
    {
      switch (Configuration.InstallFromSettings)
      {
        case AutomaticInstallFromSettings:
          Document.SelectSingleNodeOrThrow("//u:InstallFrom", NamespaceManager).RemoveSelf();
          break;
        case KeyInstallFromSettings settings:
          Document.SelectSingleNodeOrThrow("//u:InstallFrom/u:MetaData/u:Key", NamespaceManager).InnerText = settings.Key;
          Document.SelectSingleNodeOrThrow("//u:InstallFrom/u:MetaData/u:Value", NamespaceManager).InnerText = settings.Value;
          break;
        default:
          throw new NotSupportedException();
      }
    }
  }
}
