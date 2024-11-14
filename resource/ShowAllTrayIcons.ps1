if( [System.Environment]::OSVersion.Version.Build -lt 20000 ) {
	# Windows 10
	Set-ItemProperty -LiteralPath 'Registry::HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\Explorer' -Name 'EnableAutoTray' -Type 'DWord' -Value 0 -Force;
} else {
	# Windows 11
	Register-ScheduledTask -TaskName 'ShowAllTrayIcons' -Xml $(
		Get-Content -LiteralPath "C:\Windows\Setup\Scripts\ShowAllTrayIcons.xml" -Raw;
	);
}