[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\SharedQuickView\SharedQuickView.csproj'
$installerProject = Join-Path $repoRoot 'src\SharedQuickView.Installer\SharedQuickView.Installer.csproj'
$testProject = Join-Path $repoRoot 'tests\SharedQuickView.Tests\SharedQuickView.Tests.csproj'
$appOutput = Join-Path $repoRoot 'artifacts\app'
$installerOutput = Join-Path $repoRoot 'artifacts\installer'
$payloadPath = Join-Path $appOutput '共享速览.exe'

Write-Host '1/3 运行测试…' -ForegroundColor Cyan
dotnet run --project $testProject -c $Configuration
if ($LASTEXITCODE -ne 0) { throw '测试失败。' }

Write-Host '2/3 发布主程序…' -ForegroundColor Cyan
dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true -o $appOutput
if ($LASTEXITCODE -ne 0) { throw '主程序发布失败。' }

Write-Host '3/3 封装安装程序…' -ForegroundColor Cyan
dotnet publish $installerProject -c $Configuration -r $Runtime --self-contained true -o $installerOutput "-p:MainAppPayload=$payloadPath"
if ($LASTEXITCODE -ne 0) { throw '安装程序发布失败。' }

Write-Host ''
Write-Host '构建完成：' -ForegroundColor Green
Write-Host "  主程序：$payloadPath"
Write-Host "  安装包：$(Join-Path $installerOutput '共享速览安装程序.exe')"
