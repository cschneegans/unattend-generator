if( [System.Environment]::OSVersion.Version.Build -lt 20000 ) {
	# Windows 10
	reg.exe load 'HKU\DefaultUser' 'C:\Users\Default\NTUSER.DAT';
	Set-ItemProperty -LiteralPath 'Registry::HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\Explorer' -Name 'EnableAutoTray' -Type 'DWord' -Value 0 -Force;
	reg.exe unload 'HKU\DefaultUser';
} else {
	# Windows 11
	Register-ScheduledTask -TaskName 'ShowAllTrayIcons' -Xml $(
		Get-Content -LiteralPath "C:\Windows\Setup\Scripts\ShowAllTrayIcons.xml" -Raw;
	);
}