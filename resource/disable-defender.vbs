Set fso = CreateObject( "Scripting.FileSystemObject" )
Set dic = CreateObject( "Scripting.Dictionary" )
initialized = false
Do
    For Each drive In fso.Drives
        If drive.IsReady Then
            If drive.DriveLetter <> "X" Then
                For Each folder In Array( "$Windows.~BT\NewOS\Windows", "Windows" )
                    file = fso.BuildPath( fso.BuildPath( drive.RootFolder, folder ), "System32\config\SYSTEM" )
                    If fso.FileExists( file ) And fso.FileExists( file + ".LOG1" ) And fso.FileExists( file + ".LOG2" ) Then
                        If Not initialized Then
                            dic.Add file, Nothing
                        ElseIf Not dic.Exists( file ) Then
                            Set shell = CreateObject( "WScript.Shell" )
                            ret = 1
                            Do
                                WScript.Sleep 500
                                ret = shell.Run( "reg.exe LOAD HKLM\mount " + file, 0, True )
                            Loop While ret > 0
                            For Each service In Array( "Sense", "WdBoot", "WdFilter", "WdNisDrv", "WdNisSvc", "WinDefend" )
                                ret = shell.Run( "reg.exe ADD HKLM\mount\ControlSet001\Services\" + service + " /v Start /t REG_DWORD /d 4 /f", 0, True )
                            Next
                            ret = shell.Run( "reg.exe UNLOAD HKLM\mount", 0, True )
                            Exit Do
                        End If
                    End If
                Next
            End If
        End If
    Next
    initialized = true
    WScript.Sleep 1000
Loop