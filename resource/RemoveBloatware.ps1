$installed = & $getCommand;
foreach( $selector in $selectors ) {
	$result = [ordered] @{
		Selector = $selector;
	};
	if( $found = $installed | Where-Object -FilterScript $filterCommand ) {
		$result.Output = $found | & $removeCommand;
		if( $? ) {
			$result.Message = "${type} removed.";
		} else {
			$result.Message = "${type} not removed.";
			$result.Error = $Error[0];
		}
	} else {
		$result.Message = "${type} not installed.";
	}
	$result | ConvertTo-Json -Depth 3 -Compress;
}