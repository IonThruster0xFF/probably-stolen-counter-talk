param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen', [switch]$Install)
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot\src\CounterTalk\CounterTalk.csproj" -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project "$PSScriptRoot\tests\CounterTalk.Tests\CounterTalk.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
dotnet run --project "$PSScriptRoot\tests\Bridge.Tests\Bridge.Tests.csproj" -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Bridge tests failed' }
$destination = Join-Path $PSScriptRoot 'dist\CounterTalk'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\src\CounterTalk\bin\Release\netstandard2.1\CounterTalk.dll" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot\src\CounterTalk\manifest.xml" -Destination $destination
if ($Install) {
    if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) { throw 'Close the game before installing' }
    $modDestination = Join-Path $GameDir 'Mods\CounterTalk'
    New-Item -ItemType Directory -Force -Path $modDestination | Out-Null
    Copy-Item -LiteralPath "$destination\CounterTalk.dll", "$destination\manifest.xml" -Destination $modDestination
    Write-Output "Installed: $modDestination"
}
