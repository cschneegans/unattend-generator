WScript.Echo "Scanning for newly created SYSTEM registry hive file to disable Windows Defender services..."
Set fso = CreateObject( "Scripting.FileSystemObject" )
Set dic = CreateObject( "Scripting.Dictionary" )
initialized = false

Function Execute( command )
    WScript.Echo "Running command '" + command + "'"
    Set shell = CreateObject( "WScript.Shell" )
    Set exec = shell.Exec( command )
    Do While exec.Status = 0
         WScript.Sleep 100
    Loop
    WScript.Echo exec.StdOut.ReadAll
    WScript.Echo exec.StdErr.ReadAll
    Execute = exec.ExitCode
End Function

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
                                ret = Execute( "reg.exe LOAD HKLM\mount " + file )
                            Loop While ret > 0
                            For Each service In Array( "Sense", "WdBoot", "WdFilter", "WdNisDrv", "WdNisSvc", "WinDefend" )
                                ret = Execute( "reg.exe ADD HKLM\mount\ControlSet001\Services\" + service + " /v Start /t REG_DWORD /d 4 /f" )
                            Next
                            ret = Execute( "reg.exe UNLOAD HKLM\mount" )
                            WScript.Echo "Found SYSTEM registry hive file at '" + file + "'. This window will now close."
                            WScript.Sleep 5000
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