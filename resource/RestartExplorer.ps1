Get-Process -Name 'explorer' -ErrorAction 'SilentlyContinue' | Where-Object -FilterScript {
	$_.SI -eq ( Get-Process -Id $PID ).SI;
} | Stop-Process -Force;