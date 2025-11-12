using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using System.Xml;

namespace Schneegans.Unattend.Tests;

[TestClass]
public class WingetModifierTests
{
  [TestMethod]
  public void TestWingetModifier()
  {
    UnattendGenerator generator = new();
    Configuration config = Configuration.Default with
    {
      Winget = new WingetSettings(
        Packages: ImmutableList.Create("Microsoft.PowerToys", "7zip.7zip")
      )
    };

    XmlDocument xml = generator.GenerateXml(config);

    ModifierContext context = new(
      Configuration: config,
      Document: xml,
      NamespaceManager: new XmlNamespaceManager(xml.NameTable),
      Generator: generator,
      SpecializeScript: new SpecializeSequence(),
      FirstLogonScript: new FirstLogonSequence(),
      UserOnceScript: new UserOnceSequence(),
      DefaultUserScript: new DefaultUserSequence()
    );

    WingetModifier modifier = new(context);
    modifier.Process();

    string script = context.FirstLogonScript.GetScript();

    Assert.IsTrue(script.Contains("winget install --id Microsoft.PowerToys"));
    Assert.IsTrue(script.Contains("winget install --id 7zip.7zip"));
  }
}
