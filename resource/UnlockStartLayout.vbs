HKU = &H80000003
Set reg = GetObject("winmgmts://./root/default:StdRegProv")
Set fso = CreateObject("Scripting.FileSystemObject")

If reg.EnumKey(HKU, "", sids) = 0 Then
	If Not IsNull(sids) Then
		For Each sid In sids
			If reg.GetStringValue(HKU, sid + "\Volatile Environment", "USERPROFILE", userprofile) = 0 Then
				Set folder = fso.GetFolder(userprofile)
				If DateDiff("s", folder.DateCreated, Now) > 30 Then
					reg.SetDWORDValue HKU, sid + "\Software\Policies\Microsoft\Windows\Explorer", "LockedStartLayout", 0
				End If
			End If
		Next
	End If
End If