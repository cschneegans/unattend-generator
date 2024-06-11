& {
	foreach( $letter in 'DEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray() ) {
		$exe = "${letter}:\virtio-win-guest-tools.exe";
		if( Test-Path -LiteralPath $exe ) {
			Start-Process -FilePath $exe -ArgumentList '/passive', '/norestart' -Wait;
			return;
		}
	}
	'VirtIO Guest Tools image (virtio-win-*.iso) is not attached to this VM.';
} *>&1 >> "$env:TEMP\VirtIoGuestTools.log";