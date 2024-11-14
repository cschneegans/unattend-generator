$excludes = Get-ChildItem -LiteralPath 'Registry::HKU\DefaultUser\AppEvents\EventLabels' |
    Where-Object -FilterScript { ($_ | Get-ItemProperty).ExcludeFromCPL -eq 1; } |
    Select-Object -ExpandProperty 'PSChildName';
Get-ChildItem -Path 'Registry::HKU\DefaultUser\AppEvents\Schemes\Apps\*\*' |
    Where-Object -Property 'PSChildName' -NotIn $excludes |
    Get-ChildItem -Include '.Current' | Set-ItemProperty -Name '(Default)' -Value '';