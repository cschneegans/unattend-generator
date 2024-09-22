Set shell = CreateObject( "WScript.Shell" )
Set exec = shell.Exec( "reg.exe QUERY ""HKCU\Control Panel\NotifyIconSettings""" )
Set re = New RegExp
re.Pattern = "^HKEY_CURRENT_USER\\Control Panel\\NotifyIconSettings\\\d+$"
While Not exec.StdOut.AtEndOfStream
	line = exec.StdOut.ReadLine
	If re.Test( line ) Then
		shell.RegWrite line + "\IsPromoted", 1, "REG_DWORD"
	End If
Wend