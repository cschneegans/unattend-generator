$params = @{
	Path = 'Registry::HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32';
	ErrorAction = 'SilentlyContinue';
	Force = $true;
};
New-Item @params;
Set-ItemProperty @params -Name '(Default)' -Value '' -Type 'String';