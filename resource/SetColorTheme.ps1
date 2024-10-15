& {
	$params = @{
		LiteralPath = 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';
		Force = $true;
		Type = 'DWord';
	};
	Set-ItemProperty @params -Name 'SystemUsesLightTheme' -Value $lightThemeSystem;
	Set-ItemProperty @params -Name 'AppsUseLightTheme' -Value $lightThemeApps;
	Set-ItemProperty @params -Name 'ColorPrevalence' -Value $accentColorOnStart;
	Set-ItemProperty @params -Name 'EnableTransparency' -Value $enableTransparency;
};
& {
	Add-Type -AssemblyName 'System.Drawing';
	$accentColor = [System.Drawing.ColorTranslator]::FromHtml( $htmlAccentColor );
										
	function ConvertTo-DWord {
		param(
			[System.Drawing.Color]
			$Color
		);
						
		[byte[]] $bytes = @(
			$Color.R;
			$Color.G;
			$Color.B;
			$Color.A;
		);
		return [System.BitConverter]::ToUInt32( $bytes, 0); 
	}
					
	$startColor = [System.Drawing.Color]::FromArgb( 0xD2, $accentColor );
	Set-ItemProperty -LiteralPath 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -Value( ConvertTo-DWord -Color $accentColor ) -Type 'DWord' -Force;
	Set-ItemProperty -LiteralPath 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -Value( ConvertTo-DWord -Color $accentColor ) -Type 'DWord' -Force;
	Set-ItemProperty -LiteralPath 'Registry::HKCU\Software\Microsoft\Windows\DWM' -Name 'AccentColor' -Value( ConvertTo-DWord -Color $accentColor ) -Type 'DWord' -Force;
	$params = @{
		LiteralPath = 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent';
		Name = 'AccentPalette';
	};
	$palette = Get-ItemPropertyValue @params;
	$index = 20;
	$palette[ $index++ ] = $accentColor.R;
	$palette[ $index++ ] = $accentColor.G;
	$palette[ $index++ ] = $accentColor.B;
	$palette[ $index++ ] = $accentColor.A;
	Set-ItemProperty @params -Value $palette -Type 'Binary' -Force;
};