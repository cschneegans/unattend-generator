$params = @{
	LiteralPath = 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';
	Force = $true;
	Type = 'DWord';
};
Set-ItemProperty @params -Name 'SystemUsesLightTheme' -Value $lightThemeSystem;
Set-ItemProperty @params -Name 'AppsUseLightTheme' -Value $lightThemeApps;
Set-ItemProperty @params -Name 'ColorPrevalence' -Value $accentColorOnStart;
Set-ItemProperty @params -Name 'EnableTransparency' -Value $enableTransparency;
