param([string]$SdkPath = '', [switch]$SkipTests, [switch]$SkipWindowTests, [string]$BuildId = '')
$ErrorActionPreference = 'Stop'
if (-not $SdkPath) { $SdkPath = Split-Path -Parent (Get-Command dotnet.exe -ErrorAction Stop).Source }
$dotnet = Join-Path $SdkPath 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw '未找到 .NET 10 SDK。请通过 -SdkPath 指定 SDK 根目录。' }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.cache\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.cache\nuget'
Push-Location -LiteralPath $PSScriptRoot
try {
    $props = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)
    $version = [string]$props.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '无效的产品版本。' }
    if (-not $BuildId) { $BuildId = Get-Date -Format 'yyyyMMdd-HHmmss' }
    if ($BuildId -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,63}$') { throw '构建标识仅支持 1–64 位英文字母、数字及连字符。' }
    $tag = $version + '-build-' + $BuildId
    $artifact = Join-Path $PSScriptRoot ('artifacts\WorkIsland-' + $version + '-win-x64-build-' + $BuildId)
    if (Test-Path -LiteralPath $artifact) { throw '该构建目录已存在，不能覆盖旧产物；请使用新的 -BuildId。版本号保持不变。' }
    if (-not $SkipTests -and ((Test-Path -LiteralPath (Join-Path $PSScriptRoot ('test-results\unit-tests-' + $tag + '.json'))) -or (Test-Path -LiteralPath (Join-Path $PSScriptRoot ('test-results\audit-fixes-' + $tag))))) { throw '该构建的测试证据已存在，请使用新的 -BuildId，避免复用隔离夹具。' }
    & $dotnet restore Tools\IconAssets\IconAssets.csproj --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --verbosity minimal
    if ($LASTEXITCODE) { throw '图标工具还原失败' }
    & $dotnet build Tools\IconAssets\IconAssets.csproj -c Release --no-restore --nologo
    if ($LASTEXITCODE) { throw '图标工具构建失败' }
    & $dotnet Tools\IconAssets\bin\Release\net10.0-windows\IconAssets.dll WorkIsland.App\Assets\WorkIsland.ico
    if ($LASTEXITCODE) { throw '图标生成失败' }
    foreach ($project in @('WorkIsland.App\WorkIsland.App.csproj', 'WorkIsland.Tests\WorkIsland.Tests.csproj', 'WorkIsland.App.Tests\WorkIsland.App.Tests.csproj')) {
        & $dotnet restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --verbosity minimal
        if ($LASTEXITCODE) { throw "还原失败: $project" }
        & $dotnet build $project -c Release -f $(if($project -like 'WorkIsland.Tests*'){'net10.0'}else{'net10.0-windows'}) --no-restore --nologo
        if ($LASTEXITCODE) { throw "构建失败: $project" }
    }
    if (-not $SkipTests) {
        & $dotnet 'WorkIsland.Tests\bin\Release\net10.0\WorkIsland.Tests.dll' --output ('test-results\unit-tests-' + $tag + '.json')
        if ($LASTEXITCODE) { throw '规则/边界测试失败' }
        if (-not $SkipWindowTests) {
            & $dotnet 'WorkIsland.App.Tests\bin\Release\net10.0-windows\WorkIsland.App.Tests.dll' ('test-results\audit-fixes-' + $tag)
            if ($LASTEXITCODE) { throw '通知/图标周期回归失败' }
        } else { Write-Host '窗口回归未运行（-SkipWindowTests）；不能作为发布验收通过。' }
    }
    & $dotnet publish 'WorkIsland.App\WorkIsland.App.csproj' -c Release -f net10.0-windows --no-restore --self-contained false -o $artifact --nologo
    if ($LASTEXITCODE) { throw '打包失败' }
    $runtime = Join-Path $artifact 'runtime'
    New-Item -ItemType Directory -Path (Join-Path $runtime 'shared') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $SdkPath 'host') -Destination $runtime -Recurse -Force
    foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        $destination = Join-Path $runtime ('shared\' + $framework)
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $SdkPath ('shared\' + $framework)) | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $SdkPath 'LICENSE.txt'), (Join-Path $SdkPath 'ThirdPartyNotices.txt') -Destination $runtime -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $artifact -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $artifact -Force
    Write-Host "可运行产物: $artifact\WorkIsland.exe"
} finally { Pop-Location }
