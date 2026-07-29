$exe = "F:\new\WINHELP\dist\WINHELP.exe"
Get-Process WINHELP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$before = Get-Date
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 10
if ($p.HasExited) {
    Write-Output "CRASHED within 10s. ExitCode=$($p.ExitCode)"
} else {
    Write-Output "ALIVE pid=$($p.Id) after 10s"
    $p.Kill()
}
Write-Output "=== Recent Application event log (.NET Runtime / Application Error) since $before ==="
Get-EventLog -LogName Application -After $before -ErrorAction SilentlyContinue |
    Where-Object { $_.Source -like '*NET*' -or $_.Source -eq 'Application Error' -or $_.EntryType -eq 'Error' } |
    Select-Object -First 15 TimeGenerated, Source, EntryType, @{n='Msg';e={($_.Message -split "`n" | Select-Object -First 18) -join ' | '}} |
    Format-List
Write-Output "=== END ==="
