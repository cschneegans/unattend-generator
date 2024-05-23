New-PSDrive -PSProvider 'Registry' -Root 'HKEY_USERS' -Name 'HKU';
$excludes = Get-ChildItem -LiteralPath "HKU:\${mountKey}\AppEvents\EventLabels" |
    Where-Object -FilterScript { ($_ | Get-ItemProperty).ExcludeFromCPL -eq 1; } |
    Select-Object -ExpandProperty 'PSChildName';
Get-ChildItem -Path "HKU:\${mountKey}\AppEvents\Schemes\Apps\*\*" |
    Where-Object -Property 'PSChildName' -NotIn $excludes |
    Get-ChildItem -Include '.Current' | Set-ItemProperty -Name '(default)' -Value '';
Remove-PSDrive -Name 'HKU';