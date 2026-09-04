namespace Schneegans.Unattend;

static class Scripts
{
  public const string JapaneseKeyboardSpecialize = @"$regPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters';
if (!(Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null;
}
Set-ItemProperty -Path $regPath -Name 'LayerDriver JPN' -Value 'kbd106.dll' -Type String -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardIdentifier' -Value 'PCAT_106KEY' -Type String -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardSubtype' -Value 2 -Type DWord -Force;
Set-ItemProperty -Path $regPath -Name 'OverrideKeyboardType' -Value 7 -Type DWord -Force;";

  public const string JapaneseKeyboardFirstLogon = @"$regPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\i8042prt\Parameters';
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
}
