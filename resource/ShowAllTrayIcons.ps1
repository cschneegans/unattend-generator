Set-Location -LiteralPath 'HKCU:\';
Get-Item -Path 'HKCU:\Control Panel\NotifyIconSettings\*' -ErrorAction 'SilentlyContinue' | ForEach-Object -Process {
	$_ | Set-ItemProperty -Name 'IsPromoted' -Value 1 -Type 'DWord';
};