HKLM = &H80000002
key = "SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"
Set reg = GetObject("winmgmts://./root/default:StdRegProv")
current = Hour(Now)
reg.SetDWORDValue HKLM, key, "ActiveHoursStart", ( current + 23 ) Mod 24
reg.SetDWORDValue HKLM, key, "ActiveHoursEnd", ( current + 11 ) Mod 24
reg.SetDWORDValue HKLM, key, "SmartActiveHoursState", 2