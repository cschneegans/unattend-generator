Get-Process -Name 'explorer' -ErrorAction 'SilentlyContinue' | Where-Object -FilterScript {
	$_.SessionId -eq ( Get-Process -Id $PID ).SessionId;
} | Stop-Process -Force;