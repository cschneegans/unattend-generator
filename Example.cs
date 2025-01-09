using System;
using System.Collections.Immutable;
using System.Xml;
using System.IO;

namespace Schneegans.Unattend;

/// <summary>
/// This file demonstrates how to use the generator as a stand-alone application. To run this code, change the project's 
/// output type from ‘Class Library’ to ‘Console Application’ in Visual Studio.
/// </summary>
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
          LocaleAndKeyboard: new LocaleAndKeyboard(
            generator.Lookup<UserLocale>("en-US"),
            generator.Lookup<KeyboardIdentifier>("00000409")
          ),
          LocaleAndKeyboard2: null,
          LocaleAndKeyboard3: null,
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

    string path = Environment.ExpandEnvironmentVariables(@"%TEMP%\autounattend.xml");
    File.WriteAllBytes(path, UnattendGenerator.Serialize(xml));
  }
}
