using System;
using System.IO;
using System.Text;
using System.Xml.Schema;
using System.Xml;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;

namespace Schneegans.Unattend;

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

  internal static XmlSchema XmlSchemaFromResource(string name)
  {
    return XmlSchema.Read(XmlReaderFromResource(name), null) ?? throw new NullReferenceException();
  }

  internal static XmlSchemaSet ToSchemaSet(XmlSchema schema)
  {
    var schemas = new XmlSchemaSet();
    schemas.Add(schema);
    return schemas;
  }

  internal static void ValidateAgainstSchema(XmlDocument doc, string schemaName)
  {
    var schema = XmlSchemaFromResource(schemaName);
    {
      string? expected = schema.TargetNamespace;
      string? actual = doc.DocumentElement?.NamespaceURI;
      if (expected != actual)
      {
        throw new XmlSchemaValidationException($"Namespace URI of root element must be '{expected}', but was '{actual}'.");
      }
    }
    var settings = new XmlReaderSettings()
    {
      ValidationType = ValidationType.Schema,
      Schemas = ToSchemaSet(schema),
    };
    using var reader = XmlReader.Create(new XmlNodeReader(doc), settings);
    while (reader.Read())
    {
    }
  }

  internal static IEnumerable<string> SplitLines(string s)
  {
    return SplitLines(new StringReader(s));
  }

  internal static IEnumerable<string> SplitLines(TextReader reader)
  {
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
      yield return line;
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

  public static string ToPrettyString(XmlDocument doc)
  {
    using var sw = new StringWriter();
    using var writer = XmlWriter.Create(sw, new XmlWriterSettings()
    {
      CloseOutput = true,
      OmitXmlDeclaration = true,
      Indent = true,
      IndentChars = "\t",
      NewLineChars = "\r\n",
    });
    doc.Save(writer);
    writer.Close();
    return sw.ToString();
  }

  public static byte[] ToPrettyBytes(XmlDocument doc)
  {
    using var mstr = new MemoryStream();
    using var writer = XmlWriter.Create(mstr, new XmlWriterSettings()
    {
      Encoding = Encoding.UTF8,
      CloseOutput = true,
      Indent = true,
      IndentChars = "\t",
      NewLineChars = "\r\n",
    });
    doc.Save(writer);
    writer.Close();
    return mstr.ToArray();
  }

  public static void AddXmlFile(XmlDocument xml, string path, XmlDocument doc, XmlNamespaceManager ns)
  {
    AddFile(ToPrettyString(xml), useCDataSection: true, path, doc, ns);
  }

  public static void AddTextFile(string content, string path, XmlDocument doc, XmlNamespaceManager ns)
  {
    AddFile(content, useCDataSection: false, path, doc, ns);
  }

  private static void AddFile(string content, bool useCDataSection, string path, XmlDocument doc, XmlNamespaceManager ns)
  {
    {
      XmlNode root = doc.SelectSingleNodeOrThrow("/u:unattend", ns);
      XmlNode? extensions = root.SelectSingleNode("s:Extensions", ns);
      if (extensions == null)
      {
        extensions = doc.CreateElement("Extensions", Constants.MyNamespaceUri);
        root.AppendChild(extensions);

        XmlNode extractScript = doc.CreateElement("ExtractScript", Constants.MyNamespaceUri);
        extensions.AppendChild(extractScript);
        extractScript.InnerText = StringFromResource("ExtractScripts.ps1");

        CommandAppender appender = new(doc, ns, CommandConfig.Specialize);
        appender.Append(
          CommandBuilder.PowerShellCommand(@"$xml = [xml]::new(); $xml.Load('C:\Windows\Panther\unattend.xml'); $sb = [scriptblock]::Create( $xml.unattend.Extensions.ExtractScript ); Invoke-Command -ScriptBlock $sb -ArgumentList $xml;")
        );
      }

      XmlElement file = doc.CreateElement("File", Constants.MyNamespaceUri);
      file.SetAttribute("path", path);
      extensions.AppendChild(file);

      if (useCDataSection)
      {
        file.AppendChild(doc.CreateCDataSection("\r\n" + content + "\r\n")); // Safe to add newline because document is serialized without XML declaration.
      }
      else
      {
        file.InnerText = content;
      }
    }
  }
}