using System.Xml;

namespace Schneegans.Unattend;

/// <summary>
/// 日本語 (ja-JP) および 日本語 106 キーボード環境向け特化 Modifier
/// </summary>
[TargetLocale("ja-JP", KeyboardIds = ["00000411", "0411"])]
class JaJpModifier(ModifierContext context) : LocaleSpecificModifier(context)
{
  private const string JapaneseKeyboardSpecialize = @"$regPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters';
if (!(Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null;
}
Set-ItemProperty -Path $regPath -Name 'LayerDriver JPN' -Value 'kbd106.dll' -Type String -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardIdentifier' -Value 'PCAT_106KEY' -Type String -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardSubtype' -Value 2 -Type DWord -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardType' -Value 7 -Type DWord -Force;";

  private const string JapaneseKeyboardFirstLogon = @"$regPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters';
if (!(Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null;
}
Set-ItemProperty -Path $regPath -Name 'LayerDriver JPN' -Value 'kbd106.dll' -Type String -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardIdentifier' -Value 'PCAT_106KEY' -Type String -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardSubtype' -Value 2 -Type DWord -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardType' -Value 7 -Type DWord -Force;
try {
    $langList = New-WinUserLanguageList -Language 'ja-JP';
    Set-WinUserLanguageList -LanguageList $langList -Force;
    Copy-UserInternationalSettingsToSystem -WelcomeScreen $true -NewUser $true;
} catch {}";

  public override void Process()
  {
    // 1. WinPE の component に LayeredDriver="1" (日本語106キーボードドライバ) を注入
    var componentPe = Document.SelectSingleNode(
      "//u:component[@name = 'Microsoft-Windows-International-Core-WinPE']",
      NamespaceManager
    );

    if (componentPe != null && componentPe.SelectSingleNode("u:LayeredDriver", NamespaceManager) == null)
    {
      NewSimpleElement("LayeredDriver", (XmlElement)componentPe, "1");
    }

    // 2. Specialize パスへのレジストリ設定 (i8042prt 106キーボード設定)
    SpecializeScript.Append(JapaneseKeyboardSpecialize);

    // 3. FirstLogon パスへの設定 (レジストリ設定 + 言語リスト強制適用)
    FirstLogonScript.Append(JapaneseKeyboardFirstLogon);
  }
}


