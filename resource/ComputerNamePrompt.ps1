Add-Type -AssemblyName 'Microsoft.VisualBasic';
function Test-ComputerName {
	[CmdletBinding()]
	param(
		[string]
		$Value
	);
	
	if( [string]::IsNullOrWhiteSpace( $Value ) ) {
		return $false;
	}
	
	if( $Value.Length -gt 15 ) {
		return $false;
	}
	
	if( $Value -match '\s' ) {
		return $false;
	}
	
	if( $Value -match '^\d+$' ) {
		return $false;
	}

	if( $Value.IndexOfAny( '{|}~[\]^'':;<=>?@!"#$%`()+/.,*&'.ToCharArray() ) -gt -1 ) {
		return $false;
	}
	
	return $true;
}
while( $true ) {
	$title = 'Computer name';
	$candidate = [Microsoft.VisualBasic.Interaction]::InputBox( 'Enter a valid computer name:', $title, $candidate );
	if( Test-ComputerName -Value $candidate ) {
		return $candidate;
	} else {
		[Microsoft.VisualBasic.Interaction]::MsgBox( "'${candidate}' is not a valid computer name.", 'Critical, OkOnly', $title ) | Out-Null;
	}
}