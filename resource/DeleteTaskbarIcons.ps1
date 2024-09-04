reg.exe load 'HKU\DefaultUser' 'C:\Users\Default\NTUSER.DAT';

$command = 'powershell.exe -NoProfile -Command "{0}"' -f { Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband' '*'; Get-Process 'explorer' | Where-Object { $_.SI -eq (Get-Process -Id $PID).SI; } | Stop-Process; };
Set-ItemProperty -LiteralPath 'Registry::HKU\DefaultUser\Software\Microsoft\Windows\CurrentVersion\RunOnce' -Name 'DeleteTaskbarIcons' -Type 'String' -Value $command -Force;

reg.exe unload 'HKU\DefaultUser';
