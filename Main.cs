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

record class CommandConfig(
  Func<XmlDocument, XmlNamespaceManager, XmlElement> CreateElement
)
{
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
  public static CommandConfig WindowsPE = new(
    CreateElement: (doc, ns) =>
    {
      var container = Util.GetOrCreateElement(Pass.windowsPE, "Microsoft-Windows-Setup", "RunSynchronous", doc, ns);
      var outer = Util.NewElement("RunSynchronousCommand", container, doc, ns);
      return Util.NewElement("Path", outer, doc, ns);
    }
  );

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
  public static CommandConfig Specialize = new(
    CreateElement: (doc, ns) =>
    {
      var container = Util.GetOrCreateElement(Pass.specialize, "Microsoft-Windows-Deployment", "RunSynchronous", doc, ns);
      var outer = Util.NewElement("RunSynchronousCommand", container, doc, ns);
      return Util.NewElement("Path", outer, doc, ns);
    }
  );

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
  public static CommandConfig Oobe = new(
    CreateElement: (doc, ns) =>
    {
      var container = Util.GetOrCreateElement(Pass.oobeSystem, "Microsoft-Windows-Shell-Setup", "FirstLogonCommands", doc, ns);
      var outer = Util.NewElement("RunSynchronousCommand", container, doc, ns);
      return Util.NewElement("CommandLine", outer, doc, ns);
    }
  );
}

class CommandAppender(XmlDocument doc, XmlNamespaceManager ns, CommandConfig config)
{
  public void Command(string value)
  {
    config.CreateElement(doc, ns).InnerText = value;
  }

  public void ShellCommand(string command)
  {
    Command($@"cmd.exe /c ""{command}""");
  }

  /// <summary>
  /// Runs a command and redirects its <c>stdout</c> and <c>stderr</c> to a file.
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

    foreach (string line in Util.SplitLines(sb.ToString()))
    {
      WriteToFile(path, line);
    }
  }

  public void WriteToFile(string path, string line)
  {
    Command($@"cmd.exe /c "">>""{path}"" echo {EscapeShell(line)}""");
  }

  public void WriteToFile(string path, IEnumerable<string> lines)
  {
    foreach (string line in lines)
    {
      WriteToFile(path, line);
    }
  }
}

public class ConfigurationException(string? message) : Exception(message);

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

public interface IKeyed
{
  string Key { get; }
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
  string displayName,
  string? token,
  ImmutableList<BloatwareStep> steps,
  string? since
) : IKeyed
{
  public string DisplayName { get; } = displayName;

  public string Key { get; } = $"Remove{token ?? displayName.Replace(" ", "")}";

  public string? Since { get; } = since;

  public ImmutableList<BloatwareStep> Steps { get; } = steps;

  [JsonIgnore]
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

public class Component(
  string name,
  ImmutableSortedSet<Pass> passes
) : IKeyed
{
  public string Name { get; } = ValidateName(name);

  private static readonly Regex Pattern = new("^[a-z-]+$", RegexOptions.IgnoreCase);

  private static string ValidateName(string name)
  {
    ArgumentNullException.ThrowIfNull(name);
    if (!Pattern.IsMatch(name))
    {
      throw new ArgumentException($"Name '{name}' contains illegal characters.", nameof(name));
    }
    return name;
  }

  public ImmutableSortedSet<Pass> Passes { get; } = passes;

  public string Key => Name;

  public string Uri
  {
    get
    {
      string name = Name switch
      {
        "Microsoft-Windows-WDF-KernelLibrary" => "microsoft-windows-wdf-kernel-library",
        "Microsoft-Windows-MapControl-Desktop" => "microsoft-windows-mapcontrol",
        _ => Name.ToLowerInvariant(),
      };
      return $"https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/{name}";
    }
  }
}

public class WindowsEdition(
  string value,
  string displayName,
  string productKey,
  bool visible
) : IKeyed
{
  public string Value { get; } = value;

  public string DisplayName { get; } = displayName;

  public string ProductKey { get; } = productKey;

  [JsonIgnore]
  public string Key => Value;

  public bool Visible = visible;
}

public class ImageLanguage(
  string tag,
  string displayName
) : IKeyed
{
  public string Tag { get; } = tag;

  public string DisplayName { get; } = displayName;

  [JsonIgnore]
  public string Key => Tag;
}

public class KeyboardIdentifier(
  string code,
  string displayName
) : IKeyed
{
  public string Code { get; } = code;

  public string DisplayName { get; } = displayName;

  [JsonIgnore]
  public string Key => Code;
}

public class TimeOffset(
  string id,
  string displayName
) : IKeyed
{
  public string Id { get; } = id;

  public string DisplayName { get; } = displayName;

  [JsonIgnore]
  public string Key => Id;
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
  string code,
  string displayName,
  KeyboardIdentifier? defaultKeyboardLayout,
  FormattingExamples? formattingExamples
) : IKeyed
{
  public string Code { get; } = code;

  public string DisplayName { get; } = displayName;

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
      JsonSerializerSettings settings = new()
      {
        TypeNameHandling = TypeNameHandling.Auto,
      };
      Bloatwares = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<Bloatware[]>(json, settings) ?? throw new NullReferenceException()
      );
    }
    {
      string json = Util.StringFromResource("Component.json");
      Components = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<Component[]>(json) ?? throw new NullReferenceException()
      );
    }
    {
      string json = Util.StringFromResource("ImageLanguage.json");
      ImageLanguages = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<ImageLanguage[]>(json) ?? throw new NullReferenceException()
      );
    }
    {
      string json = Util.StringFromResource("KeyboardIdentifier.json");
      KeyboardIdentifiers = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<KeyboardIdentifier[]>(json) ?? throw new NullReferenceException()
      );
    }
    {
      string json = Util.StringFromResource("UserLocale.json");
      UserLocales = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<UserLocale[]>(json) ?? throw new NullReferenceException()
      );
    }
    {
      string json = Util.StringFromResource("WindowsEdition.json");
      WindowsEditions = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<WindowsEdition[]>(json) ?? throw new NullReferenceException()
      );
    }
    {
      string json = Util.StringFromResource("TimeOffset.json");
      TimeZones = ImmutableList.CreateRange(
        JsonConvert.DeserializeObject<TimeOffset[]>(json) ?? throw new NullReferenceException()
      );
    }

    {
      VerifyUniqueKeys(Components, e => e.Key);
    }
    {
      VerifyUniqueKeys(WindowsEditions, e => e.DisplayName);
      VerifyUniqueKeys(WindowsEditions, e => e.Value);
      VerifyUniqueKeys(WindowsEditions, e => e.ProductKey);
    }
    {
      VerifyUniqueKeys(UserLocales, e => e.Code);
      VerifyUniqueKeys(UserLocales, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(KeyboardIdentifiers, e => e.Code);
      VerifyUniqueKeys(KeyboardIdentifiers, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(ImageLanguages, e => e.Tag);
      VerifyUniqueKeys(ImageLanguages, e => e.DisplayName);
    }
    {
      VerifyUniqueKeys(TimeZones, e => e.Id);
      VerifyUniqueKeys(TimeZones, e => e.DisplayName);
    }
    {
      //VerifyUniqueKeys(Enum.GetValues<ProcessorArchitectures>(), e => e.Description());
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
    return found ?? throw new ArgumentException($"Could not find an element of type '{nameof(T)}' with key '{key}'.");
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
      return (T)(object)Lookup(TimeZones, key);
    }
    if (typeof(T) == typeof(Bloatware))
    {
      return (T)(object)Lookup(Bloatwares, key);
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
      new TimeZoneModifier(context),
      new ComputerNameModifier(context),
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
