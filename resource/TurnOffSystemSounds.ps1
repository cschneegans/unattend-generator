$excludes = Get-ChildItem -LiteralPath "Registry::${mountKey}\AppEvents\EventLabels" |
    Where-Object -FilterScript { ($_ | Get-ItemProperty).ExcludeFromCPL -eq 1; } |
    Select-Object -ExpandProperty 'PSChildName';
Get-ChildItem -Path "Registry::${mountKey}\AppEvents\Schemes\Apps\*\*" |
    Where-Object -Property 'PSChildName' -NotIn $excludes |
    Get-ChildItem -Include '.Current' | Set-ItemProperty -Name '(default)' -Value '';