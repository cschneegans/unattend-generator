$ErrorActionPreference = 'Stop';
Set-StrictMode -Version 'Latest';
& {
	$newName = ( Get-Content -LiteralPath 'C:\Windows\Setup\Scripts\ComputerName.txt' -Raw ).Trim();
	if( [string]::IsNullOrWhitespace( $newName ) ) {
		throw "No computer name was provided.";
	}

	$keys = @(
		@{
			LiteralPath = 'Registry::HKLM\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName';
			Name = 'ComputerName';
		};
		@{
			LiteralPath = 'Registry::HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters';
			Name = 'Hostname';
		};
		@{
			LiteralPath = 'Registry::HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters';
			Name = 'NV Hostname';
		};
	);

	while( $true ) {
		foreach( $key in $keys ) {
			Set-ItemProperty @key -Type 'String' -Value $newName;
		}
		Start-Sleep -Milliseconds 50;
	}
} *>&1 | Out-String -Width 1KB -Stream >> 'C:\Windows\Setup\Scripts\SetComputerName.log';