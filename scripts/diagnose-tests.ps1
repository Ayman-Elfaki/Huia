param(
    [string]$SearchDir = "src/dotnet/tests",
    [string]$File = "",
    [int]$Top = 25
)

$files = if ($File -ne "") {
    @(Get-Item $File)
} else {
    @(Get-ChildItem -Recurse -Filter "*.trx" -Path $SearchDir -ErrorAction SilentlyContinue)
}

if ($files.Count -eq 0) {
    Write-Warning "No .trx test result files found in '$SearchDir'. Run dotnet test with --logger 'trx' first."
    exit 0
}

$results = @()

foreach ($f in $files) {
    [xml]$xml = Get-Content $f.FullName
    $ns = @{ ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010" }
    $nodes = Select-Xml -Xml $xml -XPath "//ns:UnitTestResult" -Namespace $ns
    foreach ($node in $nodes) {
        $elem = $node.Node
        $durStr = $elem.duration
        if ($durStr) {
            $ts = [TimeSpan]::Parse($durStr)
            $parts = $elem.testName -split '\.'
            $className = if ($parts.Length -gt 1) { $parts[-2] } else { 'Unknown' }
            $results += [PSCustomObject]@{
                Project = $f.Directory.Parent.Name
                Class = $className
                TestName = $parts[-1]
                DurationSec = [math]::Round($ts.TotalSeconds, 3)
                Outcome = $elem.outcome
            }
        }
    }
}

Write-Host "=================== TOP $Top LONGEST TESTS ==================="
$results | Sort-Object DurationSec -Descending | Select-Object -First $Top | Format-Table Project, Class, DurationSec, TestName -AutoSize

Write-Host "`n=================== SLOWEST TEST CLASSES ==================="
$results | Group-Object Class | Select-Object Name, Count,
    @{Name="TotalSec"; Expression={[math]::Round(($_.Group | Measure-Object -Property DurationSec -Sum).Sum, 2)}},
    @{Name="AvgSec"; Expression={[math]::Round(($_.Group | Measure-Object -Property DurationSec -Average).Average, 2)}},
    @{Name="MaxSec"; Expression={[math]::Round(($_.Group | Measure-Object -Property DurationSec -Maximum).Maximum, 2)}} |
    Sort-Object TotalSec -Descending | Select-Object -First 20 | Format-Table -AutoSize

Write-Host "`n=================== SUMMARY BY PROJECT ==================="
$results | Group-Object Project | Select-Object Name, Count,
    @{Name="TotalSec"; Expression={[math]::Round(($_.Group | Measure-Object -Property DurationSec -Sum).Sum, 2)}},
    @{Name="AvgSec"; Expression={[math]::Round(($_.Group | Measure-Object -Property DurationSec -Average).Average, 3)}} |
    Format-Table -AutoSize
