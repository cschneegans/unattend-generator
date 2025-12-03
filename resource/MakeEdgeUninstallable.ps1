$ErrorActionPreference = 'Stop';
& {
	try {
		$params = @{
			LiteralPath = 'C:\Windows\System32\IntegratedServicesRegionPolicySet.json';
			Encoding = 'Utf8';
		};
		$o = Get-Content @params | ConvertFrom-Json;
		$o.policies | ForEach-Object -Process {
			if( $_.guid -eq '{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}' ) {
				$_.defaultState = 'enabled';
			}
		};
		$o | ConvertTo-Json -Depth 9 | Out-File @params;
	} catch {
		$_;
	}
} *>&1 | Out-String -Width 1KB -Stream >> 'C:\Windows\Setup\Scripts\MakeEdgeUninstallable.log';