Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband' -Name '*';
Get-Process -Name 'explorer' -ErrorAction 'SilentlyContinue' | Where-Object -FilterScript {
	$_.SI -eq ( Get-Process -Id $PID ).SI;
} | Stop-Process -Force;