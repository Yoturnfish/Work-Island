param(
    [string]$SdkPath='',
    [string]$BuildId='',
    [string]$PackageSource='',
    [switch]$SkipTests,
    [switch]$SkipWindowTests
)
$ErrorActionPreference='Stop'
if(!$SdkPath){
    $command=Get-Command dotnet.exe -ErrorAction SilentlyContinue
    $SdkPath=if($command){Split-Path $command.Source}else{[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\tools\dotnet-10.0.401'))}
}
$dotnet=Join-Path $SdkPath 'dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){throw 'Specify a .NET 10 SDK directory with -SdkPath. End users do not need this SDK.'}
if(!$BuildId){$BuildId=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-net48'}
if($BuildId -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,63}$'){throw 'Invalid build ID'}
$props=[xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)
$version=[string]$props.Project.PropertyGroup[0].Version
if(!$version){$version=[string]($props.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version}
$artifact=Join-Path $PSScriptRoot ('artifacts\WorkIsland-'+$version+'-win-x64-build-'+$BuildId)
$report=Join-Path $PSScriptRoot ('test-results\compat-build-'+$BuildId+'.json')
$zip=Join-Path $PSScriptRoot ('artifacts\WorkIsland-'+$version+'-win-x64-'+$BuildId+'-portable.zip')
$staging=Join-Path $PSScriptRoot ('.cache\compat-publish-'+$BuildId)
foreach($path in @($artifact,$report,$zip,$staging)){if(Test-Path -LiteralPath $path){throw ('Preserving existing output: '+$path)}}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'test-results'),(Join-Path $PSScriptRoot 'artifacts') -Force|Out-Null
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.cache\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.cache\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Push-Location -LiteralPath $PSScriptRoot
try{
    $projects=@('WorkIsland.App')
    if(!$SkipTests){$projects+=@('WorkIsland.Tests','WorkIsland.App.Tests')}
    foreach($project in $projects){
        $restore=@('restore',"$project\$project.csproj",'-p:TargetFramework=net48','--verbosity','minimal')
        if($PackageSource){$restore+=@('--source',$PackageSource)}
        & $dotnet @restore
        if($LASTEXITCODE){throw ('Restore failed: '+$project)}
        & $dotnet build "$project\$project.csproj" -c Release -f net48 --no-restore --nologo
        if($LASTEXITCODE){throw ('Build failed: '+$project)}
    }
    if(!$SkipTests){
        & '.\WorkIsland.Tests\bin\Release\net48\WorkIsland.Tests.exe' --output ('test-results\compat-core-'+$BuildId+'.json')
        if($LASTEXITCODE){throw 'Core checks failed'}
        if(!$SkipWindowTests){
            & '.\WorkIsland.App.Tests\bin\Release\net48\WorkIsland.App.Tests.exe' ('test-results\compat-wpf-'+$BuildId)
            if($LASTEXITCODE){throw 'Window checks failed'}
        }
    }
    & $dotnet publish WorkIsland.App\WorkIsland.App.csproj -c Release -f net48 --no-build --no-restore -o $staging --nologo
    if($LASTEXITCODE){throw 'Publish failed'}
    # Framework reference outputs also copy related PDBs during Publish.
    # Preserve the complete developer staging and copy only deployment files.
    New-Item -ItemType Directory -Path $artifact|Out-Null
    Get-ChildItem -LiteralPath $staging -Recurse -File|Where-Object {$_.Extension -ne '.pdb'}|ForEach-Object {
        $destination=Join-Path $artifact ([IO.Path]::GetRelativePath($staging,$_.FullName))
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force|Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }
    if(Test-Path -LiteralPath (Join-Path $artifact 'runtime')){throw 'Unexpected bundled runtime'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $artifact
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'COMPATIBILITY-NET48.md') -Destination (Join-Path $artifact 'README.txt')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MICROSOFT-LIBRARIES-LICENSE.txt') -Destination $artifact
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.cache\nuget\system.text.json\10.0.15\THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $artifact 'THIRD-PARTY-NOTICES.txt')
    [IO.File]::WriteAllText((Join-Path $artifact 'Start-Portable.cmd'),"@echo off`r`nstart `"`" `"%~dp0WorkIsland.exe`" --data-dir `"%~dp0Data`"`r`n",[Text.Encoding]::ASCII)
    [ordered]@{framework='net48';appBinary='WorkIsland.exe';version=$version;buildId=$BuildId;requires='.NET Framework 4.8 supplied by Windows';bundledRuntime=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $artifact 'BUILD-INFO.json') -Encoding utf8
    $manifest=@(Get-ChildItem -LiteralPath $artifact -Recurse -File | Sort-Object FullName | ForEach-Object {
        if($_.Extension -eq '.pdb'){throw 'Unexpected development symbols in portable payload'}
        $relative=[IO.Path]::GetRelativePath($artifact,$_.FullName)
        [ordered]@{file=$relative;path=$relative;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })
    $manifest|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $artifact 'FILES-SHA256.json') -Encoding utf8
    Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $artifact -Force).FullName -DestinationPath $zip -CompressionLevel Optimal
    [ordered]@{buildId=$BuildId;framework='net48';artifact=$artifact;portable=$zip;portableBytes=(Get-Item $zip).Length;payloadBytes=(Get-ChildItem $artifact -Recurse -File|Measure-Object Length -Sum).Sum;appSha256=(Get-FileHash (Join-Path $artifact 'WorkIsland.exe')).Hash;coreSha256=(Get-FileHash (Join-Path $artifact 'WorkIsland.Core.dll')).Hash;coreTestsRun=(!$SkipTests);windowTestsRun=(!$SkipTests-and !$SkipWindowTests)}|ConvertTo-Json|Set-Content -LiteralPath $report -Encoding utf8
    Write-Output $zip
}finally{Pop-Location}
