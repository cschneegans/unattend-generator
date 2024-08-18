reg.exe load 'HKU\DefaultUser' 'C:\Users\Default\NTUSER.DAT';

if( [System.Environment]::OSVersion.Version.Build -lt 20000 ) {
	# Windows 10
	Set-ItemProperty -LiteralPath 'Registry::HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\Explorer' -Name 'EnableAutoTray' -Type 'DWord' -Value 0 -Force;
} else {
	# Windows 11
	$command = 'powershell.exe -NoProfile -Command "{0}"' -f {
		Set-Location -LiteralPath 'HKCU:\';
		Get-Item -Path 'HKCU:\Control Panel\NotifyIconSettings\*' -ErrorAction 'SilentlyContinue' | ForEach-Object -Process {
			$_ | Set-ItemProperty -Name 'IsPromoted' -Value 1 -Type 'DWord';
		};
	};
	Set-ItemProperty -LiteralPath 'Registry::HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ShowAllTrayIcons' -Type 'String' -Value $command -Force;
}

reg.exe unload 'HKU\DefaultUser';