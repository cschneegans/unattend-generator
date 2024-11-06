$formatter = {
	$args[0].ToString( "yyyy'-'MM'-'dd'T'HH':'mm':'ssK" );
};
$now = [datetime]::UtcNow;
$start = & $formatter $now;
$end = & $formatter $now.AddDays( 7 );

$params = @{
	LiteralPath = 'Registry::HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings';
	Type = 'String';
	Force = $true;
};

Set-ItemProperty @params -Name 'PauseFeatureUpdatesStartTime' -Value $start;
Set-ItemProperty @params -Name 'PauseFeatureUpdatesEndTime' -Value $end;
Set-ItemProperty @params -Name 'PauseQualityUpdatesStartTime' -Value $start;
Set-ItemProperty @params -Name 'PauseQualityUpdatesEndTime' -Value $end;
Set-ItemProperty @params -Name 'PauseUpdatesStartTime' -Value $start;
Set-ItemProperty @params -Name 'PauseUpdatesExpiryTime' -Value $end;
