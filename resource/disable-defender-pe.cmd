@ECHO OFF
SET file=C:\$Windows.~BT\NewOS\Windows\System32\config\SYSTEM
FOR /L %%i IN (0) DO (
	CALL :sleep
	IF EXIST %file% (
		CALL :load
		FOR %%s IN (Sense WdBoot WdFilter WdNisDrv WdNisSvc WinDefend) DO reg.exe ADD HKLM\mount\ControlSet001\Services\%%s /v Start /t REG_DWORD /d 4 /f
		reg.exe UNLOAD HKLM\mount
		EXIT ) )

GOTO :eof

:load
	reg.exe LOAD HKLM\mount %file%
	IF %errorlevel% GTR 0 GOTO load
GOTO :eof

:sleep
	ping.exe -n 1 127.0.0.1 > NUL
GOTO :eof
