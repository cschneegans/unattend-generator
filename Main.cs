using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;

namespace Schneegans.Unattend;

public enum RecoveryModes
{
  None, Folder, Partition
}

public enum PartitionLayouts
{
  MBR, GPT
}

public enum ProcessorArchitectures
{
  x86, amd64, arm64
}

public enum ExpressSettingsMode
{
  Interactive, EnableAll, DisableAll
}

internal static class Util
{
  internal static MemoryStream LoadFromResource(string name)
  {
    Type type = typeof(Util);
    using var stream = type.Assembly.GetManifestResourceStream(type, "resource." + name) ?? throw new ArgumentException($"Resource '{name}' not found.");
    var mstr = new MemoryStream();
    stream.CopyTo(mstr);
    mstr.Seek(0, SeekOrigin.Begin);
    return mstr;
  }

  internal static string StringFromResource(string name)
  {
    using var mstr = LoadFromResource(name);
    return new StreamReader(mstr, Encoding.UTF8).ReadToEnd();
  }

  internal static XmlReader XmlReaderFromResource(string name)
  {
    return XmlReader.Create(LoadFromResource(name));
  }

  internal static XmlDocument XmlDocumentFromResource(string name)
  {
    var doc = new XmlDocument();
    doc.Load(XmlReaderFromResource(name));
    return doc;
  }

  internal static XmlSchemaSet XmlSchemaSetFromResource(string name)
  {
    XmlSchema schema = XmlSchema.Read(XmlReaderFromResource(name), null) ?? throw new NullReferenceException();
    var schemas = new XmlSchemaSet();
    schemas.Add(schema);
    return schemas;
  }

  internal static void ValidateAgainstSchema(XmlDocument doc, string schemaName)
  {
    var settings = new XmlReaderSettings()
    {
      ValidationType = ValidationType.Schema,
      Schemas = XmlSchemaSetFromResource(schemaName),
    };
    using var reader = XmlReader.Create(new XmlNodeReader(doc), settings);
    while (reader.Read())
    {
    }
  }

  public static XmlElement GetOrCreateElement(Pass pass, string component, XmlDocument doc, XmlNamespaceManager ns)
  {
    var setting = doc.SelectSingleNodeOrThrow($"/u:unattend/u:settings[@pass='{pass}']", ns);
    XmlElement? elem = (XmlElement?)setting.SelectSingleNode($"u:component[@name='{component}']", ns);
    if (elem == null)
    {
      elem = doc.CreateElement("component", ns.LookupNamespace("u"));
      elem.SetAttribute("name", component);
      elem.SetAttribute("processorArchitecture", "x86");
      elem.SetAttribute("publicKeyToken", "31bf3856ad364e35");
      elem.SetAttribute("language", "neutral");
      elem.SetAttribute("versionScope", "nonSxS");
      setting.AppendChild(elem);
    }
    return elem;
  }

  public static XmlElement GetOrCreateElement(Pass pass, string component, string element, XmlDocument doc, XmlNamespaceManager ns)
  {
    XmlElement comp = GetOrCreateElement(pass, component, doc, ns);
    XmlElement? elem = (XmlElement?)comp.SelectSingleNode($"u:{element}", ns);
    if (elem == null)
    {
      elem = doc.CreateElement(element, ns.LookupNamespace("u"));
      comp.AppendChild(elem);
    }
    return elem;
  }

  public static XmlElement NewSimpleElement(string name, XmlElement parent, string innerText, XmlDocument doc, XmlNamespaceManager ns)
  {
    XmlElement element = doc.CreateElement(name, ns.LookupNamespace("u"));
    element.InnerText = innerText;
    parent.AppendChild(element);
    return element;
  }

  public static XmlElement NewElement(string name, XmlElement parent, XmlDocument doc, XmlNamespaceManager ns)
  {
    XmlElement element = doc.CreateElement(name, ns.LookupNamespace("u"));
    parent.AppendChild(element);
    return element;
  }
}

record class CommandConfig(
  Pass Pass,
  string Component,
  string Element1,
  string Element2,
  string Element3
)
{
  // <settings pass="windowsPE">
  //   <component name="Microsoft-Windows-Setup">
  //     <RunSynchronous>
  //       <RunSynchronousCommand>
  //         <Path>
  public static CommandConfig WindowsPE = new(
    Pass: Pass.windowsPE,
    Component: "Microsoft-Windows-Setup",
    Element1: "RunSynchronous",
    Element2: "RunSynchronousCommand",
    Element3: "Path"
  );

  // <settings pass="specialize">
  //   <component name="Microsoft-Windows-Deployment">
  //     <RunSynchronous>
  //       <RunSynchronousCommand>
  //         <Path>
  public static CommandConfig Specialize = new(
    Pass: Pass.specialize,
    Component: "Microsoft-Windows-Deployment",
    Element1: "RunSynchronous",
    Element2: "RunSynchronousCommand",
    Element3: "Path"
  );

  // <settings pass="oobeSystem">
  //   <component name="Microsoft-Windows-Shell-Setup">
  //     <FirstLogonCommands>
  //       <SynchronousCommand>
  //         <CommandLine>
  public static CommandConfig Oobe = new(
    Pass: Pass.oobeSystem,
    Component: "Microsoft-Windows-Shell-Setup",
    Element1: "FirstLogonCommands",
    Element2: "SynchronousCommand",
    Element3: "CommandLine"
  );
}

class CommandAppender(XmlDocument doc, XmlNamespaceManager ns, CommandConfig config)
{
  private readonly Lazy<XmlElement> container = new(() =>
  {
    return Util.GetOrCreateElement(config.Pass, config.Component, config.Element1, doc, ns);
  });

  public void Command(string value)
  {
    var commandElement = doc.CreateElement(config.Element2, ns.LookupNamespace("u"));
    container.Value.AppendChild(commandElement);
    var pathElement = doc.CreateElement(config.Element3, ns.LookupNamespace("u"));
    commandElement.AppendChild(pathElement);
    pathElement.InnerText = value;
  }

  public void ShellCommand(string command)
  {
    Command($@"cmd.exe /c ""{command}""");
  }

  /// <summary>
  /// Runs a command and redirects its errors and output to a file.
  /// </summary>
  public void ShellCommand(string command, string outFile)
  {
    Command($@"cmd.exe /c ""2>&1 >>""{outFile}"" {command}""");
  }

  public void UserRunOnceCommand(string name, string value, string rootKey, string subKey)
  {
    static string Escape(string s)
    {
      return s.Replace(@"""", @"\""");
    }

    RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\Runonce"" /v ""{Escape(name)}"" /t REG_SZ /d ""{Escape(value)}"" /f");
  }

  public void RegistryDefaultUserCommand(Action<string, string> action)
  {
    string rootKey = "HKU";
    string subKey = "mount";
    RegistryCommand(@$"load ""{rootKey}\{subKey}"" ""C:\Users\Default\NTUSER.DAT""");
    action.Invoke(rootKey, subKey);
    RegistryCommand(@$"unload ""{rootKey}\{subKey}""");
  }

  public void RegistryCommand(string value)
  {
    Command($"reg.exe {value}");
  }

  public void PowerShellCommand(string value)
  {
    Command(GetPowerShellCommand(value));
  }

  public string GetPowerShellCommand(string value)
  {
    {
      const char quote = '"';
      if (value.Contains(quote))
      {
        throw new ArgumentException($"PowerShell command '{value}' must not contain '{quote}'.");
      }
    }
    {
      const char semicolon = ';';
      const char brace = '}';
      if (!value.EndsWith(semicolon) && !value.EndsWith(brace))
      {
        throw new ArgumentException($"PowerShell command '{value}' must end with either '{semicolon}' or '{brace}'.");
      }
    }
    return @$"powershell.exe -NoProfile -Command ""{value}""";
  }
  public void InvokePowerShellScript(string file)
  {
    PowerShellCommand($"Get-Content -LiteralPath '{file}' -Raw | Invoke-Expression;");
  }

  static string EscapeShell(string command)
  {
    return command
      .Replace("^", "^^")
      .Replace("&", "^&")
      .Replace("<", "^<")
      .Replace(">", "^>")
      .Replace("|", "^|");
  }

  public void WriteToFile(string path, XmlDocument doc)
  {
    var sb = new StringBuilder();
    using var writer = XmlWriter.Create(sb, new XmlWriterSettings()
    {
      Indent = true,
      IndentChars = "",
      OmitXmlDeclaration = true,
    });

    doc.WriteTo(writer);
    writer.Close();

    using var sr = new StringReader(sb.ToString());
    string? line;
    while ((line = sr.ReadLine()) != null)
    {
      WriteToFile(path, line);
    }
  }

  public void WriteToFile(string path, string line)
  {
    this.Command($@"cmd.exe /c "">>""{path}"" echo {EscapeShell(line)}""");
  }

  public void WriteToFile(string path, IEnumerable<string> lines)
  {
    foreach (string line in lines)
    {
      WriteToFile(path, line);
    }
  }
}

public static class Extensions
{
  public static XmlNode SelectSingleNodeOrThrow(this XmlNode node, string xpath, XmlNamespaceManager nsmgr)
  {
    return node.SelectSingleNode(xpath, nsmgr) ?? throw new NullReferenceException($"No node matches XPath '{xpath}'.");
  }

  public static XmlNode SelectSingleNodeOrThrow(this XmlNode node, string xpath)
  {
    return node.SelectSingleNode(xpath) ?? throw new NullReferenceException($"No node matches XPath '{xpath}'.");
  }

  public static IEnumerable<XmlNode> SelectNodesOrEmpty(this XmlNode node, string xpath)
  {
    XmlNodeList? result = node.SelectNodes(xpath);
    return result == null ? Array.Empty<XmlNode>() : result.Cast<XmlNode>();
  }

  public static IEnumerable<XmlNode> SelectNodesOrEmpty(this XmlNode node, string xpath, XmlNamespaceManager nsmgr)
  {
    XmlNodeList? result = node.SelectNodes(xpath, nsmgr);
    return result == null ? Array.Empty<XmlNode>() : result.Cast<XmlNode>();
  }

  public static void RemoveSelf(this XmlNode node)
  {
    node.ParentNode.RemoveChild(node);
  }

  public static string Description(this WifiAuthentications value)
  {
    return value switch
    {
      WifiAuthentications.Open => "Open",
      WifiAuthentications.WPA2PSK => "WPA2-Personal AES",
      WifiAuthentications.WPA3SAE => "WPA3-Personal AES",
      _ => throw new ArgumentException($"Illegal value '{value}'.", nameof(value)),
    };
  }

  public static string Description(this ProcessorArchitectures value)
  {
    string slashWithThinspaces = "\u2009/\u2009";

    return value switch
    {
      ProcessorArchitectures.x86 => $"Intel{slashWithThinspaces}AMD 32-bit",
      ProcessorArchitectures.amd64 => $"Intel{slashWithThinspaces}AMD 64-bit",
      ProcessorArchitectures.arm64 => "Windows on Arm64",
      _ => throw new ArgumentException($"Illegal value '{value}'.", nameof(value)),
    };
  }
}

public enum Pass
{
  offlineServicing,
  windowsPE,
  generalize,
  specialize,
  auditSystem,
  auditUser,
  oobeSystem
}

public interface IProcessAuditSettings;

public record class EnabledProcessAuditSettings(
  bool IncludeCommandLine
) : IProcessAuditSettings;

public class DisabledProcessAuditSettings : IProcessAuditSettings;

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

public interface IWdacSettings;

public class SkipWdacSettings : IWdacSettings;

public enum WdacScriptModes
{
  Restricted, Unrestricted
}

public enum WdacAuditModes
{
  Auditing, AuditingOnBootFailure, Enforcement
}

public record class ConfigureWdacSettings(
  WdacAuditModes AuditMode,
  WdacScriptModes ScriptMode
) : IWdacSettings;

public interface ILockoutSettings;

public class DefaultLockoutSettings : ILockoutSettings;

public class DisableLockoutSettings : ILockoutSettings;

public record class CustomLockoutSettings(
  int LockoutThreshold,
  int LockoutDuration,
  int LockoutWindow
) : ILockoutSettings;

public interface ILanguageSettings;

public class InteractiveLanguageSettings : ILanguageSettings;

public record class UnattendedLanguageSettings(
  ImageLanguage ImageLanguage,
  UserLocale UserLocale,
  KeyboardIdentifier InputLocale
) : ILanguageSettings;

public interface IPartitionSettings;

public class InteractivePartitionSettings : IPartitionSettings;

public interface IInstallToSettings;

public class AvailableInstallToSettings : IInstallToSettings;

public record class CustomInstallToSettings(
  int InstallToDisk,
  int InstallToPartition
) : IInstallToSettings;

public record class CustomPartitionSettings(
  string Script,
  IInstallToSettings InstallTo
) : IPartitionSettings;

public record class UnattendedPartitionSettings(
  PartitionLayouts PartitionLayout,
  RecoveryModes RecoveryMode,
  int EspSize,
  int RecoverySize
) : IPartitionSettings;

public interface IComputerNameSettings;

public class RandomComputerNameSettings : IComputerNameSettings;

public record class CustomComputerNameSettings(
  string ComputerName
) : IComputerNameSettings;

public interface ITimeZoneSettings;

public class ImplicitTimeZoneSettings : ITimeZoneSettings;

public record class ExplicitTimeZoneSettings(
  TimeOffset TimeZone
) : ITimeZoneSettings;

public interface IEditionSettings;

public record class UnattendedEditionSettings(
  WindowsEdition Edition
) : IEditionSettings;

public class InteractiveEditionSettings : IEditionSettings;

public interface IAccountSettings;

public class InteractiveAccountSettings : IAccountSettings;

public record class UnattendedAccountSettings(
  ImmutableList<Account> Accounts,
  IAutoLogonSettings AutoLogonSettings
) : IAccountSettings;

public interface IAutoLogonSettings;

public class NoneAutoLogonSettings : IAutoLogonSettings;

public record BuiltinAutoLogonSettings(
  string Password
) : IAutoLogonSettings;

public class OwnAutoLogonSettings : IAutoLogonSettings;

public record class Account(
  string Name,
  string Password,
  string Group
)
{
  public bool HasName => !string.IsNullOrWhiteSpace(Name);
}

public record class Configuration(
  ILanguageSettings LanguageSettings,
  IAccountSettings AccountSettings,
  IPartitionSettings PartitionSettings,
  ExpressSettingsMode ExpressSettings,
  IEditionSettings EditionSettings,
  ImmutableHashSet<ProcessorArchitectures> ProcessorArchitectures,
  ImmutableDictionary<string, ImmutableSortedSet<Pass>> Components,
  ImmutableList<Bloatware> Bloatwares,
  bool BypassRequirementsCheck,
  bool BypassNetworkCheck,
  bool EnableLongPaths,
  bool EnableRemoteDesktop,
  ILockoutSettings LockoutSettings,
  IProcessAuditSettings ProcessAuditSettings,
  bool HardenSystemDriveAcl,
  bool AllowPowerShellScripts,
  IComputerNameSettings ComputerNameSettings,
  ITimeZoneSettings TimeZoneSettings,
  IWifiSettings WifiSettings,
  IWdacSettings WdacSettings,
  bool DisableLastAccess,
  bool NoAutoRebootWithLoggedOnUsers,
  bool DisableDefender,
  bool DisableSystemRestore,
  bool TurnOffSystemSounds,
  bool RunScriptOnFirstLogon,
  bool DisableAppSuggestions,
  bool DisableWidgets
);

public class JsonBloatwareStep
{
  public string? Type { get; set; }

  public string? Selector { get; set; }

  public byte[]? Versions { get; set; }
}

public class JsonBloatware : IJson
{
  public string? DisplayName { get; set; }

  public string? Token { get; set; }

  public JsonBloatwareStep[]? Steps { get; set; }

  public string? Since { get; set; }
}

public abstract class BloatwareStep(
  byte[]? versions
)
{
  public ImmutableSortedSet<byte> Versions { get; } = (versions == null) ? throw new ArgumentNullException(nameof(versions)) : ImmutableSortedSet.CreateRange(versions);
}

public class SelectorBloatwareStep(
  byte[]? versions,
  string? selector
) : BloatwareStep(versions)
{
  public string Selector { get; } = selector ?? throw new ArgumentNullException(nameof(selector));
}

public class PackageBloatwareStep(
  byte[]? versions,
  string? selector
) : SelectorBloatwareStep(versions, selector);

public class CapabilityBloatwareStep(
  byte[]? versions,
  string? selector
) : SelectorBloatwareStep(versions, selector);

public class CustomBloatwareStep(
  byte[]? versions
) : BloatwareStep(versions);

public class Bloatware(
  string? displayName,
  string? token,
  ImmutableList<BloatwareStep> steps,
  string? since
) : IKeyed
{
  public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

  public string Key { get; } = $"Remove{token ?? displayName.Replace(" ", "")}";

  public string? Since { get; } = since;

  public ImmutableList<BloatwareStep> Steps { get; } = steps;

  public string? StepsLabel
  {
    get
    {
      IEnumerable<string?> selects = Steps.Select(s =>
      {
        return s switch
        {
          SelectorBloatwareStep sel => sel.Selector,
          CustomBloatwareStep => null,
          _ => throw new NotImplementedException(),
        };
      }).Where(s => !string.IsNullOrWhiteSpace(s));
      if (selects.Any())
      {
        return string.Join(", ", selects);
      }
      else
      {
        return null;
      }
    }
  }
}

public class JsonComponent : IJson
{
  public string? Component { get; set; }

  public string[]? Passes { get; set; }
}

public class Component(
  string? name,
  IEnumerable<string>? passes
) : IKeyed
{
  public string Name { get; } = ValidateName(name);

  private static string ValidateName(string? name)
  {
    ArgumentNullException.ThrowIfNull(name);
    if (!Pattern.IsMatch(name))
    {
      throw new ArgumentException($"Name '{name}' contains illegal characters.", nameof(name));
    }
    return name;
  }

  private static readonly Regex Pattern = new("^[a-z-]+$", RegexOptions.IgnoreCase);

  public ImmutableSortedSet<Pass> Passes { get; } = ValidatePasses(passes);

  private static ImmutableSortedSet<Pass> ValidatePasses(IEnumerable<string>? passes)
  {
    ArgumentNullException.ThrowIfNull(passes);
    return ImmutableSortedSet.CreateRange(
      passes.Select(Enum.Parse<Pass>)
    );
  }

  public string Key => Name;

  public string Uri
  {
    get
    {
      string name = Name;
      if (name == "Microsoft-Windows-WDF-KernelLibrary")
      {
        name = "microsoft-windows-wdf-kernel-library";
      }
      else if (name == "Microsoft-Windows-MapControl-Desktop")
      {
        name = "microsoft-windows-mapcontrol";
      }
      else
      {
        name = Name.ToLowerInvariant();
      }

      return $"https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/{name}";
    }
  }
}

public class JsonWindowsEdition : IJson
{
  public string? Value { get; set; }

  public string? DisplayName { get; set; }

  public string? ProductKey { get; set; }

  public bool Visible { get; set; }
}

public class WindowsEdition(
  string? value,
  string? displayName,
  string? productKey,
  bool visible
) : IKeyed
{
  public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

  public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

  public string ProductKey { get; } = productKey ?? throw new ArgumentNullException(nameof(productKey));

  [JsonIgnore]
  public string Key => Value;

  public bool Visible = visible;
}

public interface IJson;

public interface IKeyed
{
  string Key { get; }
}

public class JsonImageLanguage : IJson
{
  public string? Tag { get; set; }

  public string? DisplayName { get; set; }
}

public class ImageLanguage(
  string? tag,
  string? displayName
) : IKeyed
{
  public string Tag { get; } = tag ?? throw new ArgumentNullException(nameof(tag));

  public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

  [JsonIgnore]
  public string Key => Tag;
}

public class JsonKeyboardIdentifier : IJson
{
  public string? Code { get; set; }

  public string? DisplayName { get; set; }
}

public class KeyboardIdentifier(
  string? code,
  string? displayName
) : IKeyed
{
  public string Code { get; } = code ?? throw new ArgumentNullException(nameof(code));

  public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

  [JsonIgnore]
  public string Key => Code;
}

public class JsonTimeOffset : IJson
{
  public string? Id { get; set; }
  public string? DisplayName { get; set; }
}

public class TimeOffset(
  string? id,
  string? displayName
) : IKeyed
{
  public string Id { get; } = id ?? throw new ArgumentNullException(nameof(id));

  public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

  public string Key => Id;
}

public class JsonFormattingExamples : IJson
{
  public string? LongDate { get; set; }

  public string? ShortDate { get; set; }

  public string? LongTime { get; set; }

  public string? ShortTime { get; set; }

  public string? Currency { get; set; }

  public string? Number { get; set; }
}

public class JsonUserLocale : IJson
{
  public string? Code { get; set; }

  public string? DisplayName { get; set; }

  public string? KeyboardLayout { get; set; }

  public JsonFormattingExamples FormattingExamples { get; set; } = new();
}

public record class FormattingExamples(
  string LongDate,
  string ShortDate,
  string LongTime,
  string ShortTime,
  string Currency,
  string Number
);

public class UserLocale(
  string? code,
  string? displayName,
  KeyboardIdentifier? defaultKeyboardLayout,
  FormattingExamples? formattingExamples
) : IKeyed
{
  public string Code { get; } = code ?? throw new ArgumentNullException(nameof(code));

  public string DisplayName { get; } = displayName ?? throw new ArgumentNullException(nameof(displayName));

  [JsonIgnore]
  public KeyboardIdentifier? DefaultKeyboardLayout { get; } = defaultKeyboardLayout;

  public string? KeyboardLayout => DefaultKeyboardLayout?.Code;

  [JsonIgnore]
  public string Key => Code;

  public FormattingExamples FormattingExamples { get; } = formattingExamples ?? throw new ArgumentNullException(nameof(formattingExamples));
}

public static class Constants
{
  public static readonly string FirstLogonScript = @"C:\Windows\Setup\Scripts\UserFirstLogon.cmd";

  public const string UsersGroup = "Users";

  public const string AdministratorsGroup = "Administrators";

  public const string DefaultPassword = "password";

  public static readonly ushort RecoveryPartitionSize = 1000;

  public static readonly int EspDefaultSize = 300;

  public static readonly string DiskpartScript = DiskModifier.GetCustomDiskpartScript();
}

public class UnattendGenerator
{
  public UnattendGenerator()
  {
    {
      string json = Util.StringFromResource("Bloatware.json");
      var array = JsonConvert.DeserializeObject<JsonBloatware[]>(json) ?? throw new NullReferenceException();
      Bloatwares = ImmutableList.CreateRange(
        array.Select(j =>
        {
          IEnumerable<BloatwareStep> steps = (j.Steps ?? []).Select<JsonBloatwareStep, BloatwareStep>(s =>
          {
            return s.Type switch
            {
              "ProvisionedPackage" => new PackageBloatwareStep(s.Versions, s.Selector),
              "Capability" => new CapabilityBloatwareStep(s.Versions, s.Selector),
              "Custom" => new CustomBloatwareStep(s.Versions),
              _ => throw new NotSupportedException($"Unsupported type '{s.Type}'.")
            };
          });
          return new Bloatware(j.DisplayName, j.Token, ImmutableList.CreateRange(steps), j.Since);
        })
      );
    }
    {
      string json = Util.StringFromResource("Component.json");
      var array = JsonConvert.DeserializeObject<JsonComponent[]>(json) ?? throw new NullReferenceException();
      Components = ImmutableList.CreateRange(
        array.Select(j => new Component(j.Component, j.Passes))
      );
    }
    {
      string json = Util.StringFromResource("ImageLanguage.json");
      var array = JsonConvert.DeserializeObject<JsonImageLanguage[]>(json) ?? throw new NullReferenceException();
      ImageLanguages = ImmutableList.CreateRange(
        array.Select(j => new ImageLanguage(j.Tag, j.DisplayName))
      );
    }
    {
      string json = Util.StringFromResource("KeyboardIdentifier.json");
      var array = JsonConvert.DeserializeObject<JsonKeyboardIdentifier[]>(json) ?? throw new NullReferenceException();
      KeyboardIdentifiers = ImmutableList.CreateRange(
        array.Select(j => new KeyboardIdentifier(j.Code, j.DisplayName))
      );
    }
    {
      string json = Util.StringFromResource("UserLocale.json");
      var array = JsonConvert.DeserializeObject<JsonUserLocale[]>(json) ?? throw new NullReferenceException();

      UserLocales = ImmutableList.CreateRange(
        array.Select(j =>
        {
          FormattingExamples examples = new(
            Currency: j.FormattingExamples.Currency ?? throw new NullReferenceException("currency"),
            ShortTime: j.FormattingExamples.ShortTime ?? throw new NullReferenceException("shortTime"),
            LongTime: j.FormattingExamples.LongTime ?? throw new NullReferenceException("longTime"),
            ShortDate: j.FormattingExamples.ShortDate ?? throw new NullReferenceException("shortDate"),
            LongDate: j.FormattingExamples.LongDate ?? throw new NullReferenceException("longDate"),
            Number: j.FormattingExamples.Number ?? throw new NullReferenceException("number")
          );
          KeyboardIdentifier? kid = j.KeyboardLayout == null ? null : Lookup<KeyboardIdentifier>(j.KeyboardLayout);
          return new UserLocale(j.Code, j.DisplayName, kid, examples);
        })
      );
    }
    {
      string json = Util.StringFromResource("WindowsEdition.json");
      var array = JsonConvert.DeserializeObject<JsonWindowsEdition[]>(json) ?? throw new NullReferenceException();
      WindowsEditions = ImmutableList.CreateRange(
        array.Select(j => new WindowsEdition(j.Value, j.DisplayName, j.ProductKey, j.Visible))
      );
    }
    {
      string json = Util.StringFromResource("TimeOffset.json");
      var array = JsonConvert.DeserializeObject<JsonTimeOffset[]>(json) ?? throw new NullReferenceException();
      TimeZones = ImmutableList.CreateRange(
        array.Select(j => new TimeOffset(j.Id, j.DisplayName))
      );
    }

    {
      VerifyUniqueKeys(this.Components, e => e.Key);
    }
    {
      VerifyUniqueKeys(this.WindowsEditions, e => e.DisplayName);
      VerifyUniqueKeys(this.WindowsEditions, e => e.Value);
      VerifyUniqueKeys(this.WindowsEditions, e => e.ProductKey);
    }
    {
      VerifyUniqueKeys(this.UserLocales, e => e.Code);
      VerifyUniqueKeys(this.UserLocales, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(this.KeyboardIdentifiers, e => e.Code);
      VerifyUniqueKeys(this.KeyboardIdentifiers, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(this.ImageLanguages, e => e.Tag);
      VerifyUniqueKeys(this.ImageLanguages, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(this.TimeZones, e => e.Id);
      VerifyUniqueKeys(this.TimeZones, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(Enum.GetValues<ProcessorArchitectures>(), e => e.Description());
    }
  }

  private static void VerifyUniqueKeys<T>(IEnumerable<T> items, Func<T, object> keySelector)
  {
    items.GroupBy(keySelector).ToList().ForEach(group =>
    {
      if (group.Count() != 1)
      {
        throw new ArgumentException($"'{group.Key}' occurs more than once.");
      }
    });
  }

  public ImmutableList<TimeOffset> TimeZones { get; }

  public ImmutableList<Component> Components { get; }

  public ImmutableList<Bloatware> Bloatwares { get; }

  public ImmutableList<KeyboardIdentifier> KeyboardIdentifiers { get; }

  public ImmutableList<UserLocale> UserLocales { get; }

  public ImmutableList<ImageLanguage> ImageLanguages { get; }

  public ImmutableList<WindowsEdition> WindowsEditions { get; }

  public byte[] GenerateBytes(Configuration config)
  {
    var doc = Generate(config);
    using var mstr = new MemoryStream();
    using var writer = XmlWriter.Create(mstr, new XmlWriterSettings()
    {
      CloseOutput = true,
      Indent = true,
      IndentChars = "\t",
      NewLineChars = "\r\n",
    });
    doc.Save(writer);
    writer.Close();
    return mstr.ToArray();
  }

  [return: NotNull]
  private static T Lookup<T>(ImmutableList<T> list, string key) where T : class, IKeyed
  {
    _ = key ?? throw new ArgumentNullException(nameof(key));
    T? found = list.Find(item =>
    {
      return string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);
    });
    return found ?? throw new ArgumentException($"Could not find an element with key '{key}'.");
  }

  [return: NotNull]
  public T Lookup<T>(string key) where T : class, IKeyed
  {
    if (typeof(T) == typeof(WindowsEdition))
    {
      return (T)(object)Lookup(this.WindowsEditions, key);
    }
    if (typeof(T) == typeof(UserLocale))
    {
      return (T)(object)Lookup(this.UserLocales, key);
    }
    if (typeof(T) == typeof(ImageLanguage))
    {
      return (T)(object)Lookup(this.ImageLanguages, key);
    }
    if (typeof(T) == typeof(KeyboardIdentifier))
    {
      return (T)(object)Lookup(this.KeyboardIdentifiers, key);
    }
    if (typeof(T) == typeof(TimeOffset))
    {
      return (T)(object)Lookup(this.TimeZones, key);
    }
    if (typeof(T) == typeof(Bloatware))
    {
      return (T)(object)Lookup(this.Bloatwares, key);
    }
    throw new NotSupportedException();
  }

  public XmlDocument Generate(Configuration config)
  {
    var doc = Util.XmlDocumentFromResource("autounattend.xml");
    var ns = new XmlNamespaceManager(doc.NameTable);
    ns.AddNamespace("u", "urn:schemas-microsoft-com:unattend");
    ns.AddNamespace("wcm", "http://schemas.microsoft.com/WMIConfig/2002/State");

    ModifierContext context = new(
      Configuration: config,
      Document: doc,
      NamespaceManager: ns,
      Generator: this
    );

    new List<Modifier> {
      new DiskModifier(context),
      new BypassModifier(context),
      new ProductKeyModifier(context),
      new UsersModifier(context),
      new BloatwareModifier(context),
      new LocalesModifier(context),
      new ExpressSettingsModifier(context),
      new WifiModifier(context),
      new EmptyElementsModifier(context),
      new LockoutModifier(context),
      new OptimizationsModifier(context),
      new ComponentsModifier(context),
      new WdacModifier(context),
      new OrderModifier(context),
      new ProcessorArchitectureModifier(context),
      new PrettyModifier(context),
    }.ForEach(modifier =>
    {
      modifier.Process();
    });

    Util.ValidateAgainstSchema(doc, "autounattend.xsd");

    return doc;
  }
}

public record class ModifierContext(
  XmlDocument Document,
  XmlNamespaceManager NamespaceManager,
  Configuration Configuration,
  UnattendGenerator Generator
);

abstract class Modifier(ModifierContext context)
{
  public XmlDocument Document { get; } = context.Document;

  public XmlNamespaceManager NamespaceManager { get; } = context.NamespaceManager;

  public Configuration Configuration { get; } = context.Configuration;

  public UnattendGenerator Generator { get; } = context.Generator;

  public XmlElement NewSimpleElement(string name, XmlElement parent, string innerText)
  {
    return Util.NewSimpleElement(name, parent, innerText, Document, NamespaceManager);
  }

  public XmlElement NewElement(string name, XmlElement parent)
  {
    return Util.NewElement(name, parent, Document, NamespaceManager);
  }

  public abstract void Process();
}
