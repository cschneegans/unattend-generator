using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Schneegans.Unattend;

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

public enum RecoveryMode
{
  None, Folder, Partition
}

public enum PartitionLayout
{
  MBR, GPT
}

public enum ProcessorArchitecture
{
  x86, amd64, arm64
}

public enum ExpressSettingsMode
{
  Interactive, EnableAll, DisableAll
}

abstract class CommandConfig
{
  public readonly static WindowsPECommandConfig WindowsPE = new();
  public readonly static SpecializeCommandConfig Specialize = new();
  public readonly static OobeCommandConfig Oobe = new();

  public abstract XmlElement CreateElement(XmlDocument doc, XmlNamespaceManager ns);
}

/// <summary>
/// Inserts a command using this markup:
/// <code>
/// &lt;settings pass=&quot;windowsPE&quot;&gt;
///   &lt;component name=&quot;Microsoft-Windows-Setup&quot;&gt;
///     &lt;RunSynchronous&gt;
///       &lt;RunSynchronousCommand&gt;
///         &lt;Path&gt;
/// </code>
/// </summary>
class WindowsPECommandConfig : CommandConfig
{
  public override XmlElement CreateElement(XmlDocument doc, XmlNamespaceManager ns)
  {
    var container = Util.GetOrCreateElement(Pass.windowsPE, "Microsoft-Windows-Setup", "RunSynchronous", doc, ns);
    var outer = Util.NewElement("RunSynchronousCommand", container, doc, ns);
    return Util.NewElement("Path", outer, doc, ns);
  }
}

/// <summary>
/// Inserts a command using this markup:
/// <code>
/// &lt;settings pass=&quot;specialize&quot;&gt;
///   &lt;component name=&quot;Microsoft-Windows-Deployment&quot;&gt;
///     &lt;RunSynchronous&gt;
///       &lt;RunSynchronousCommand&gt;
///         &lt;Path&gt;
/// </code>
/// </summary>
class SpecializeCommandConfig : CommandConfig
{
  public override XmlElement CreateElement(XmlDocument doc, XmlNamespaceManager ns)
  {
    var container = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Deployment", "RunSynchronous", doc, ns);
    var outer = Util.NewElement("RunSynchronousCommand", container, doc, ns);
    return Util.NewElement("Path", outer, doc, ns);
  }
}

/// <summary>
/// Inserts a command using this markup:
/// <code>
/// &lt;settings pass=&quot;oobeSystem&quot;&gt;
///   &lt;component name=&quot;Microsoft-Windows-Shell-Setup&quot;&gt;
///     &lt;FirstLogonCommands&gt;
///       &lt;SynchronousCommand&gt;
///         &lt;CommandLine&gt;
/// </code>
/// </summary>
class OobeCommandConfig : CommandConfig
{
  public override XmlElement CreateElement(XmlDocument doc, XmlNamespaceManager ns)
  {
    var container = Util.GetOrCreateElement(Pass.oobeSystem, "Microsoft-Windows-Shell-Setup", "FirstLogonCommands", doc, ns);
    var outer = Util.NewElement("SynchronousCommand", container, doc, ns);
    return Util.NewElement("CommandLine", outer, doc, ns);
  }
}

class CommandAppender(XmlDocument doc, XmlNamespaceManager ns, CommandConfig config)
{
  public void Append(string value)
  {
    config.CreateElement(doc, ns).InnerText = value;
  }

  public void Append(IEnumerable<string> values)
  {
    foreach (string value in values)
    {
      Append(value);
    }
  }
}

static class CommandBuilder
{
  public static string Raw(string command)
  {
    return command;
  }

  public static string ShellCommand(string command)
  {
    return $@"cmd.exe /c ""{command}""";
  }

  /// <summary>
  /// Runs a command and redirects its <c>stdout</c> and <c>stderr</c> to a file.
  /// </summary>
  public static string ShellCommand(string command, string outFile)
  {
    return $@"cmd.exe /c ""2>&1 >>""{outFile}"" {command}""";
  }

  public static string RegistryCommand(string value)
  {
    return $"reg.exe {value}";
  }

  public static string UserRunOnceCommand(string name, string value, string rootKey, string subKey)
  {
    static string Escape(string s)
    {
      return s.Replace(@"""", @"\""");
    }

    return RegistryCommand(@$"add ""{rootKey}\{subKey}\Software\Microsoft\Windows\CurrentVersion\Runonce"" /v ""{Escape(name)}"" /t REG_SZ /d ""{Escape(value)}"" /f");
  }

  public static IEnumerable<string> RegistryDefaultUserCommand(Func<string, string, IEnumerable<string>> action)
  {
    string rootKey = "HKU";
    string subKey = "mount";
    return [
      RegistryCommand(@$"load ""{rootKey}\{subKey}"" ""C:\Users\Default\NTUSER.DAT"""),
      .. action.Invoke(rootKey, subKey),
      RegistryCommand(@$"unload ""{rootKey}\{subKey}"""),
    ];
  }

  public static string PowerShellCommand(string value)
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

  public static string InvokePowerShellScript(string filepath)
  {
    return PowerShellCommand($"Get-Content -LiteralPath '{filepath}' -Raw | Invoke-Expression;");
  }

  public static string InvokeVBScript(string filepath)
  {
    return @$"cscript.exe //E:vbscript ""{filepath}""";
  }

  public static string InvokeJScript(string filepath)
  {
    return @$"cscript.exe //E:jscript ""{filepath}""";
  }

  public static IEnumerable<string> WriteToFile(string path, XmlDocument doc)
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

    return Util.SplitLines(sb.ToString()).SelectMany(line => WriteToFile(path, line));
  }

  public static IEnumerable<string> WriteToFile(string path, string line)
  {
    static string EscapeShell(string command)
    {
      return command
        .Replace("^", "^^")
        .Replace("&", "^&")
        .Replace("<", "^<")
        .Replace(">", "^>")
        .Replace("|", "^|")
        .Replace("%", "^%");
    }

    if (string.IsNullOrWhiteSpace(line))
    {
      yield return $@"cmd.exe /c "">>""{path}"" echo({line}""";
    }
    else
    {
      int chunkSize = 256 - 64 - path.Length;
      var chunks = line.Chunk(chunkSize).Select(chars => new string(chars));
      foreach (var chunk in chunks.SkipLast(1))
      {
        yield return $@"cmd.exe /c "">>""{path}"" <nul set /p={EscapeShell(chunk)}""";
      }
      yield return $@"cmd.exe /c "">>""{path}"" echo {EscapeShell(chunks.Last())}""";
    }
  }

  public static IEnumerable<string> WriteToFile(string path, IEnumerable<string> lines)
  {
    return lines.SelectMany(line => WriteToFile(path, line));
  }
}

public class ConfigurationException(string? message) : Exception(message);

public record class Configuration(
  ILanguageSettings LanguageSettings,
  IAccountSettings AccountSettings,
  IPartitionSettings PartitionSettings,
  IEditionSettings EditionSettings,
  ILockoutSettings LockoutSettings,
  IPasswordExpirationSettings PasswordExpirationSettings,
  IProcessAuditSettings ProcessAuditSettings,
  IComputerNameSettings ComputerNameSettings,
  ITimeZoneSettings TimeZoneSettings,
  IWifiSettings WifiSettings,
  IWdacSettings WdacSettings,
  ImmutableHashSet<ProcessorArchitecture> ProcessorArchitectures,
  ImmutableDictionary<string, ImmutableSortedSet<Pass>> Components,
  ImmutableList<Bloatware> Bloatwares,
  ExpressSettingsMode ExpressSettings,
  ScriptSettings ScriptSettings,
  bool BypassRequirementsCheck,
  bool BypassNetworkCheck,
  bool EnableLongPaths,
  bool EnableRemoteDesktop,
  bool HardenSystemDriveAcl,
  bool AllowPowerShellScripts,
  bool DisableLastAccess,
  bool NoAutoRebootWithLoggedOnUsers,
  bool DisableDefender,
  bool DisableSystemRestore,
  bool TurnOffSystemSounds,
  bool RunScriptOnFirstLogon,
  bool DisableAppSuggestions,
  bool DisableWidgets
)
{
  public static Configuration Default => new(
    LanguageSettings: new InteractiveLanguageSettings(),
    AccountSettings: new InteractiveAccountSettings(),
    PartitionSettings: new InteractivePartitionSettings(),
    EditionSettings: new InteractiveEditionSettings(),
    LockoutSettings: new DefaultLockoutSettings(),
    PasswordExpirationSettings: new DefaultPasswordExpirationSettings(),
    ProcessAuditSettings: new DisabledProcessAuditSettings(),
    ComputerNameSettings: new RandomComputerNameSettings(),
    TimeZoneSettings: new ImplicitTimeZoneSettings(),
    WifiSettings: new InteractiveWifiSettings(),
    WdacSettings: new SkipWdacSettings(),
    ProcessorArchitectures: [Unattend.ProcessorArchitecture.amd64],
    Components: ImmutableDictionary.Create<string, ImmutableSortedSet<Pass>>(),
    Bloatwares: [],
    ExpressSettings: ExpressSettingsMode.DisableAll,
    ScriptSettings: new ScriptSettings([]),
    BypassRequirementsCheck: false,
    BypassNetworkCheck: false,
    EnableLongPaths: false,
    EnableRemoteDesktop: false,
    HardenSystemDriveAcl: false,
    AllowPowerShellScripts: false,
    DisableLastAccess: false,
    NoAutoRebootWithLoggedOnUsers: false,
    DisableDefender: false,
    DisableSystemRestore: false,
    TurnOffSystemSounds: false,
    RunScriptOnFirstLogon: false,
    DisableAppSuggestions: false,
    DisableWidgets: false
  );
}

public interface IKeyed
{
  string Id { get; }
}

public abstract class BloatwareStep(
  byte[] versions
)
{
  public ImmutableSortedSet<byte> Versions { get; } = ImmutableSortedSet.CreateRange(versions);
}

public class SelectorBloatwareStep(
  byte[] versions,
  string selector
) : BloatwareStep(versions)
{
  public string Selector { get; } = selector;
}

public class PackageBloatwareStep(
  byte[] versions,
  string selector
) : SelectorBloatwareStep(versions, selector);

public class CapabilityBloatwareStep(
  byte[] versions,
  string selector
) : SelectorBloatwareStep(versions, selector);

public class OptionalFeatureBloatwareStep(
  byte[] versions,
  string selector
) : SelectorBloatwareStep(versions, selector);

public class CustomBloatwareStep(
  byte[] versions
) : BloatwareStep(versions);

public class Bloatware(
  string displayName,
  string? token,
  ImmutableList<BloatwareStep> steps,
  string? since
) : IKeyed
{
  public string DisplayName { get; } = displayName;

  public string Id { get; } = $"Remove{token ?? displayName.Replace(" ", "")}";

  public string? Since { get; } = since;

  public ImmutableList<BloatwareStep> Steps { get; } = steps;

  [JsonIgnore]
  public string? StepsLabel
  {
    get
    {
      IEnumerable<string> strings = Steps.SelectMany<BloatwareStep, string>(s => s switch
        {
          SelectorBloatwareStep sel => [sel.Selector],
          CustomBloatwareStep => [],
          _ => throw new NotImplementedException(),
        }
      );
      return strings.Any() ? string.Join(", ", strings) : null;
    }
  }
}

public class Component(
  string id,
  ImmutableSortedSet<Pass> passes
) : IKeyed
{
  public string Id { get; } = Validate(id);

  private static readonly Regex Pattern = new("^[a-z-]+$", RegexOptions.IgnoreCase);

  private static string Validate(string id)
  {
    if (!Pattern.IsMatch(id))
    {
      throw new ArgumentException($"ID '{id}' contains illegal characters.", nameof(id));
    }
    return id;
  }

  public ImmutableSortedSet<Pass> Passes { get; } = passes;

  public string Uri
  {
    get
    {
      string name = Id switch
      {
        "Microsoft-Windows-WDF-KernelLibrary" => "microsoft-windows-wdf-kernel-library",
        "Microsoft-Windows-MapControl-Desktop" => "microsoft-windows-mapcontrol",
        _ => Id.ToLowerInvariant(),
      };
      return $"https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/{name}";
    }
  }
}

public class WindowsEdition(
  string id,
  string displayName,
  string productKey,
  bool visible
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;

  public string ProductKey { get; } = productKey;

  public bool Visible = visible;
}

public class ImageLanguage(
  string id,
  string displayName
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;
}

public class KeyboardIdentifier(
  string id,
  string displayName
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;
}

public class TimeOffset(
  string id,
  string displayName
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;
}

public class GeoLocation(
  string id,
  string displayName
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;
}

public record class FormattingExamples(
  string LongDate,
  string ShortDate,
  string LongTime,
  string ShortTime,
  string Currency,
  string Number
);

public class KeyboardConverter(
  UnattendGenerator generator
) : JsonConverter<KeyboardIdentifier>
{
  public override bool CanWrite => false;

  public override KeyboardIdentifier? ReadJson(JsonReader reader, Type objectType, KeyboardIdentifier? existingValue, bool hasExistingValue, JsonSerializer serializer)
  {
    return reader.TokenType switch
    {
      JsonToken.String => generator.Lookup<KeyboardIdentifier>("" + reader.Value),
      JsonToken.Null => null,
      _ => throw new NotSupportedException(),
    };
  }

  public override void WriteJson(JsonWriter writer, KeyboardIdentifier? value, JsonSerializer serializer)
  {
    throw new NotImplementedException();
  }
}

public class GeoLocationConverter(
  UnattendGenerator generator
) : JsonConverter<GeoLocation>
{
  public override bool CanWrite => false;

  public override GeoLocation? ReadJson(JsonReader reader, Type objectType, GeoLocation? existingValue, bool hasExistingValue, JsonSerializer serializer)
  {
    return reader.TokenType switch
    {
      JsonToken.String => generator.Lookup<GeoLocation>("" + reader.Value),
      JsonToken.Null => null,
      _ => throw new NotSupportedException(),
    };
  }

  public override void WriteJson(JsonWriter writer, GeoLocation? value, JsonSerializer serializer)
  {
    throw new NotImplementedException();
  }
}

public class UserLocale(
  string id,
  string displayName,
  KeyboardIdentifier? keyboardLayout,
  FormattingExamples formattingExamples,
  GeoLocation? geoLocation
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;

  public KeyboardIdentifier? KeyboardLayout { get; } = keyboardLayout;

  public FormattingExamples FormattingExamples { get; } = formattingExamples;

  public GeoLocation? GeoLocation { get; } = geoLocation;
}

public static class Constants
{
  public const string FirstLogonScript = @"C:\Windows\Setup\Scripts\UserFirstLogon.cmd";

  public const string UsersGroup = "Users";

  public const string AdministratorsGroup = "Administrators";

  public const string DefaultPassword = "password";

  public const int RecoveryPartitionSize = 1000;

  public const int EspDefaultSize = 300;

  public static readonly string DiskpartScript = DiskModifier.GetCustomDiskpartScript();

  public const string MyNamespaceUri = "https://schneegans.de/windows/unattend-generator/";
}

public class UnattendGenerator
{
  public UnattendGenerator()
  {
    {
      string json = Util.StringFromResource("Bloatware.json");
      JsonSerializerSettings settings = new()
      {
        TypeNameHandling = TypeNameHandling.Auto,
      };
      Bloatwares = JsonConvert.DeserializeObject<Bloatware[]>(json, settings).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("Component.json");
      Components = JsonConvert.DeserializeObject<Component[]>(json).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("ImageLanguage.json");
      ImageLanguages = JsonConvert.DeserializeObject<ImageLanguage[]>(json).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("KeyboardIdentifier.json");
      KeyboardIdentifiers = JsonConvert.DeserializeObject<KeyboardIdentifier[]>(json).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("GeoId.json");
      GeoLocations = JsonConvert.DeserializeObject<GeoLocation[]>(json).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("UserLocale.json");
      JsonConverter[] converters = [new KeyboardConverter(this), new GeoLocationConverter(this)];
      UserLocales = JsonConvert.DeserializeObject<UserLocale[]>(json, converters).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("WindowsEdition.json");
      WindowsEditions = JsonConvert.DeserializeObject<WindowsEdition[]>(json).ToKeyedDictionary();
    }
    {
      string json = Util.StringFromResource("TimeOffset.json");
      TimeOffsets = JsonConvert.DeserializeObject<TimeOffset[]>(json).ToKeyedDictionary();
    }

    {
      VerifyUniqueKeys(Components.Values, e => e.Id);
    }
    {
      VerifyUniqueKeys(WindowsEditions.Values, e => e.Id);
      VerifyUniqueKeys(WindowsEditions.Values, e => e.DisplayName);
      VerifyUniqueKeys(WindowsEditions.Values, e => e.ProductKey);
    }
    {
      VerifyUniqueKeys(UserLocales.Values, e => e.Id);
      VerifyUniqueKeys(UserLocales.Values, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(KeyboardIdentifiers.Values, e => e.Id);
      VerifyUniqueKeys(KeyboardIdentifiers.Values, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(ImageLanguages.Values, e => e.Id);
      VerifyUniqueKeys(ImageLanguages.Values, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(TimeOffsets.Values, e => e.Id);
      VerifyUniqueKeys(TimeOffsets.Values, e => e.DisplayName);
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

  public UnattendedLanguageSettings CreateUnattendedLanguageSettings(string imageLanguage, string userLocale, string keyboardIdentifier, string geoLocation)
  {
    return new UnattendedLanguageSettings(
      ImageLanguage: Lookup<ImageLanguage>(imageLanguage),
      UserLocale: Lookup<UserLocale>(userLocale),
      InputLocale: Lookup<KeyboardIdentifier>(keyboardIdentifier),
      GeoLocation: Lookup<GeoLocation>(geoLocation)
    );
  }

  public UnattendedEditionSettings CreateUnattendedEditionSettings(string edition)
  {
    return new UnattendedEditionSettings(
      Edition: Lookup<WindowsEdition>(edition)
    );
  }

  public UnattendedPartitionSettings CreateUnattendedPartitionSettings(PartitionLayout layout, RecoveryMode recovery, int espSize = Constants.EspDefaultSize, int recoverySize = Constants.RecoveryPartitionSize)
  {
    return new UnattendedPartitionSettings(
      PartitionLayout: layout,
      RecoveryMode: recovery,
      EspSize: espSize,
      RecoverySize: recoverySize
    );
  }

  public ExplicitTimeZoneSettings CreateExplicitTimeZoneSettings(string id)
  {
    return new ExplicitTimeZoneSettings(
      TimeZone: Lookup<TimeOffset>(id)
    );
  }

  public IImmutableDictionary<string, TimeOffset> TimeOffsets { get; }

  public IImmutableDictionary<string, GeoLocation> GeoLocations { get; }

  public IImmutableDictionary<string, Component> Components { get; }

  public IImmutableDictionary<string, Bloatware> Bloatwares { get; }

  public IImmutableDictionary<string, KeyboardIdentifier> KeyboardIdentifiers { get; }

  public IImmutableDictionary<string, UserLocale> UserLocales { get; }

  public IImmutableDictionary<string, ImageLanguage> ImageLanguages { get; }

  public IImmutableDictionary<string, WindowsEdition> WindowsEditions { get; }

  [return: NotNull]
  private static T Lookup<T>(IImmutableDictionary<string, T> dic, string key) where T : IKeyed
  {
    if (dic.TryGetValue(key, out T? value))
    {
      return value;
    }
    else
    {
      throw new ConfigurationException($"Could not find an element of type '{nameof(T)}' with key '{key}'.");
    }
  }

  [return: NotNull]
  public T Lookup<T>(string key) where T : class, IKeyed
  {
    if (typeof(T) == typeof(WindowsEdition))
    {
      return (T)(object)Lookup(WindowsEditions, key);
    }
    if (typeof(T) == typeof(UserLocale))
    {
      return (T)(object)Lookup(UserLocales, key);
    }
    if (typeof(T) == typeof(ImageLanguage))
    {
      return (T)(object)Lookup(ImageLanguages, key);
    }
    if (typeof(T) == typeof(KeyboardIdentifier))
    {
      return (T)(object)Lookup(KeyboardIdentifiers, key);
    }
    if (typeof(T) == typeof(TimeOffset))
    {
      return (T)(object)Lookup(TimeOffsets, key);
    }
    if (typeof(T) == typeof(Bloatware))
    {
      return (T)(object)Lookup(Bloatwares, key);
    }
    if (typeof(T) == typeof(GeoLocation))
    {
      return (T)(object)Lookup(GeoLocations, key);
    }
    throw new NotSupportedException();
  }

  public XmlDocument GenerateXml(Configuration config)
  {
    var doc = Util.XmlDocumentFromResource("autounattend.xml");
    var ns = new XmlNamespaceManager(doc.NameTable);
    ns.AddNamespace("u", "urn:schemas-microsoft-com:unattend");
    ns.AddNamespace("wcm", "http://schemas.microsoft.com/WMIConfig/2002/State");
    ns.AddNamespace("s", Constants.MyNamespaceUri);

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
      new PasswordExpirationModifier(context),
      new OptimizationsModifier(context),
      new ComponentsModifier(context),
      new ComputerNameModifier(context),
      new TimeZoneModifier(context),
      new WdacModifier(context),
      new ScriptModifier(context),
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

  public byte[] GenerateBytes(Configuration config)
  {
    return Util.ToPrettyBytes(GenerateXml(config));
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
