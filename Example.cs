using System;
using System.Collections.Immutable;
using System.Xml;

namespace Schneegans.Unattend;

class Example
{
  public static void Main(string[] args)
  {
    UnattendGenerator generator = new();
    XmlDocument xml = generator.GenerateXml(
      Configuration.Default with
      {
        LanguageSettings = new UnattendedLanguageSettings(
          ImageLanguage: generator.Lookup<ImageLanguage>("en-US"),
          UserLocale: generator.Lookup<UserLocale>("en-US"),
          InputLocale: generator.Lookup<KeyboardIdentifier>("0409:00000409"),
          GeoLocation: generator.Lookup<GeoLocation>("244")
        ),
        Bloatwares = ImmutableList.CreateRange(
          [
            generator.Lookup<Bloatware>("RemoveTeams"),
            generator.Lookup<Bloatware>("RemoveOutlook"),
          ]
        ),
      }
    );
    using XmlWriter writer = XmlWriter.Create(Console.Out, new XmlWriterSettings()
    {
      CloseOutput = false,
      Indent = true,
    });
    xml.WriteTo(writer);
  }
}
