& {
	foreach( $letter in 'DEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray() ) {
		$exe = "${letter}:\VBoxWindowsAdditions.exe";
		if( Test-Path -LiteralPath $exe ) {
			$certs = "${letter}:\cert";
			Start-Process -FilePath "${certs}\VBoxCertUtil.exe" -ArgumentList "add-trusted-publisher ${certs}\vbox*.cer", "--root ${certs}\vbox*.cer"  -Wait;
			Start-Process -FilePath $exe -ArgumentList '/with_wddm', '/S' -Wait;
			return;
		}
	}
	'VBoxGuestAdditions.iso is not attached to this VM.';
} *>&1 >> "$env:TEMP\VBoxGuestAdditions.log";