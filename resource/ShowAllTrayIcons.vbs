HKCU = &H80000001
key = "Control Panel\NotifyIconSettings"
Set reg = GetObject("winmgmts://./root/default:StdRegProv")
If reg.EnumKey(HKCU, key, names) = 0 Then
	If Not IsNull(names) Then
		For Each name In names
			reg.SetDWORDValue HKCU, key + "\" + name, "IsPromoted", 1
		Next
	End If
End If
