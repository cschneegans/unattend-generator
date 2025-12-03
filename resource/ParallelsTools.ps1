& {
	foreach( $letter in 'DEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray() ) {
		$exe = "${letter}:\PTAgent.exe";
		if( Test-Path -LiteralPath $exe ) {
      # https://kb.parallels.com/116161/
      # Install without any UI, don't reboot after install, and block (process doesn't exit) until installation is complete.
      #
      # Networking will work after this, but some other features such as touch
      # id pass through and shared clipboard support won't work until reboot
			Start-Process -FilePath $exe -ArgumentList '/install_silent' -Wait;
			return;
		}
	}
	'Parallels Tools image (prl-tools-win-*.iso) is not attached to this VM.';
} *>&1 | Out-String -Width 1KB -Stream >> 'C:\Windows\Setup\Scripts\ParallelsTools.log';
