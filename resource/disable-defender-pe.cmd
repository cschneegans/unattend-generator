SET file=C:\$Windows.~BT\NewOS\Windows\System32\config\SYSTEM
FOR /L %%i IN (0) DO (
	ping.exe -n 2 127.0.0.1 > nul
	IF EXIST %file% (
		ping.exe -n 3 127.0.0.1 > nul
		reg.exe LOAD HKLM\mount %file%
		FOR %%s IN (Sense WdBoot WdFilter WdNisDrv WdNisSvc WinDefend) DO (
			reg.exe ADD HKLM\mount\ControlSet001\Services\%%s /v Start /t REG_DWORD /d 4 /f
		)
		reg.exe UNLOAD HKLM\mount
		EXIT
	)
)