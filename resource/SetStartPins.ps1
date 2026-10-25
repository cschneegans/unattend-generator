if( [System.Environment]::OSVersion.Version.Build -lt 20000 ) {
	return;
}
$key = 'Registry::HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start';
New-Item -Path $key -ItemType 'Directory' -ErrorAction 'SilentlyContinue';
Set-ItemProperty -LiteralPath $key -Name 'ConfigureStartPins' -Value $json -Type 'String';
Set-ItemProperty -LiteralPath $key -Name 'ConfigureStartPins_ProviderSet' -Value 1 -Type 'DWord';