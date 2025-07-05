# This file demonstrates how to use the library in PowerShell 7.4.x.
# Download a recent build artifact from GitHub and unzip it, or build the library yourself in Visual Studio.
# Note that you cannot use this library in Windows PowerShell 5.x.

using namespace Schneegans.Unattend;
Import-Module -Name "${env:USERPROFILE}\Downloads\net8.0\UnattendGenerator.dll";
$generator = [UnattendGenerator]::new();
$config = & {
    $o = [Configuration]::Default;
    $o.LanguageSettings = [UnattendedLanguageSettings]::new(
        $generator.Lookup[ImageLanguage]('en-US'),
        [LocaleAndKeyboard]::new(
            $generator.Lookup[UserLocale]('en-US'),
            $generator.Lookup[KeyboardIdentifier]('00000409')
        ),
        $null,
        $null,
        $generator.Lookup[GeoLocation]('244')
    );
    $o.Bloatwares = [System.Collections.Immutable.ImmutableList]::Create(
        $generator.Lookup[Bloatware]('RemoveTeams'),
        $generator.Lookup[Bloatware]('RemoveOutlook')
    );
    return $o;
};
$xml = [UnattendGenerator]::Serialize(
    $generator.GenerateXml( $config )
);
[System.IO.File]::WriteAllBytes( "${env:TEMP}\autounattend.xml", $xml );