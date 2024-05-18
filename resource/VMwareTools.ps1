& {
	foreach( $letter in 'DEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray() ) {
		$exe = "${letter}:\setup.exe";
		if( ( Get-Item -LiteralPath $exe -ErrorAction 'SilentlyContinue' | Select-Object -ExpandProperty 'VersionInfo' | Select-Object -ExpandProperty 'ProductName' ) -eq 'VMware Tools' ) {
			Start-Process -FilePath $exe -ArgumentList '/s /v /qn REBOOT=R' -Wait;
			return;
		}
	}
	'VMware Tools image (windows.iso) is not attached to this VM.';
} *>&1 >> "$env:TEMP\VMwareTools.log";