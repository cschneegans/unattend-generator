$params = @{
	LiteralPath = 'Registry::HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';
	Force = $true;
	Type = 'DWord';
};
Set-ItemProperty @params -Name 'SystemUsesLightTheme' -Value $lightThemeSystem;
Set-ItemProperty @params -Name 'AppsUseLightTheme' -Value $lightThemeApps;
Set-ItemProperty @params -Name 'ColorPrevalence' -Value $accentColorOnStart;
Set-ItemProperty @params -Name 'EnableTransparency' -Value $enableTransparency;
			
Start-Sleep -Seconds 10;
Get-Process -Name 'explorer' -ErrorAction 'SilentlyContinue' | Where-Object -FilterScript {
	$_.SI -eq ( Get-Process -Id $PID ).SI;
} | Stop-Process -Force;