param(
    [string]$Compiler='',
    [string]$PayloadDir='',
    [string]$OutputDir='',
    [string]$PackageName='',
    [string]$TestRoot='',
    [string]$TestId=''
)
$ErrorActionPreference='Stop'
if(!$Compiler){$Compiler=(Get-Command ISCC.exe -ErrorAction Stop).Source}
$projectRoot=Split-Path $PSScriptRoot
if(!$PayloadDir){$props=[xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props') -Raw);$PayloadDir=Join-Path $projectRoot ('artifacts\WorkIsland-'+$props.Project.PropertyGroup.Version+'-win-x64')}
if(!$OutputDir){$OutputDir=Join-Path $projectRoot 'artifacts'}
$PayloadDir=[IO.Path]::GetFullPath($PayloadDir);$OutputDir=[IO.Path]::GetFullPath($OutputDir)
$privateNames=@('Data','PreviewData','backups','test-results','.cache','state.json','state.json.bak','errors.log')
if(Get-ChildItem -LiteralPath $PayloadDir -Recurse -Force | Where-Object {$_.Name -in $privateNames}){throw 'Payload contains private/test data; publish to a fresh clean directory before packaging.'}
foreach($path in @($PayloadDir,$OutputDir,$TestRoot)){if($path.Contains("'")){throw 'Inno script paths cannot contain apostrophes'}}
if(!(Test-Path -LiteralPath $Compiler)){throw 'Official portable Inno Setup compiler was not found. See Installer/README.md.'}
if(!(Test-Path -LiteralPath (Join-Path $PayloadDir 'runtime\host\fxr'))){throw 'Payload has no bundled runtime'}
if($TestRoot){
    $TestRoot=[IO.Path]::GetFullPath($TestRoot)
    if(!$TestRoot.StartsWith((Join-Path $projectRoot 'test-results')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Acceptance root must be inside this project test-results'}
    $testGuid=[Guid]::Empty
    if(![Guid]::TryParseExact($TestId,'D',[ref]$testGuid) -or $TestId -eq '6B22A7C8-B238-4372-93DF-B942E8E62435'){throw 'Acceptance installer needs its own GUID'}
    $TestId=$testGuid.ToString('D').ToUpperInvariant()
}elseif($TestId){throw 'TestId cannot be used without TestRoot'}
$versionInfo=(Get-Item (Join-Path $PayloadDir 'WorkIsland.exe')).VersionInfo
$version=$versionInfo.FileVersion
if(!$version){throw 'Payload has no executable version'}
if(!$PackageName){$PackageName='WorkIsland-'+$version+'-win-x64-setup'}
if($PackageName -notmatch '^[A-Za-z0-9._-]+$'){throw 'Invalid package filename'}
$package=Join-Path $OutputDir ($PackageName+'.exe')
if(Test-Path -LiteralPath $package){throw ('Preserving existing installer: '+$package)}
$scriptHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'WorkIsland.iss')).Hash
$manifestPath=Join-Path $PayloadDir 'FILES-SHA256.json'
$manifestHash=$null;$manifestEntries=$null
function Assert-PayloadManifest {
    if(!$manifestEntries){return}
    if((Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash){throw 'Payload manifest changed during compilation'}
    $actualFiles=@(Get-ChildItem -LiteralPath $PayloadDir -Recurse -File -Force | Where-Object Name -ne 'FILES-SHA256.json')
    if($actualFiles.Count -ne $manifestEntries.Count){throw 'Payload contains files outside its frozen manifest'}
    foreach($entry in $manifestEntries){
        $path=[IO.Path]::GetFullPath((Join-Path $PayloadDir $entry.file))
        if(!$path.StartsWith($PayloadDir+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escapes payload'}
        if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256){throw ('Payload file differs from manifest: '+$entry.file)}
    }
}
if(Test-Path -LiteralPath $manifestPath){
    $manifestHash=(Get-FileHash -LiteralPath $manifestPath).Hash
    $manifestEntries=@(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
    Assert-PayloadManifest
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$arguments=@('/Qp',('/DPayloadDir='+$PayloadDir),('/DOutputDir='+$OutputDir),('/DProductVersion='+$version),('/DPackageName='+$PackageName))
if($TestRoot){$arguments+=@(('/DTestRoot='+$TestRoot),('/DTestId='+$TestId))}
$arguments+=Join-Path $PSScriptRoot 'WorkIsland.iss'
& $Compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$package=Join-Path $OutputDir ($PackageName+'.exe')
if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'WorkIsland.iss')).Hash -ne $scriptHash){throw 'Installer source changed during compilation'}
Assert-PayloadManifest
$source=@{compiler=$Compiler;compilerVersion=(Get-Item $Compiler).VersionInfo.FileVersion;compilerSha256=(Get-FileHash $Compiler).Hash;scriptSha256=$scriptHash;payload=$PayloadDir;payloadManifestSha256=$manifestHash;payloadFiles=if($manifestEntries){$manifestEntries.Count}else{$null};appVersion=$version;appSha256=(Get-FileHash (Join-Path $PayloadDir 'WorkIsland.dll')).Hash;installer=$package;installerSha256=(Get-FileHash $package).Hash;acceptanceRoot=$TestRoot;acceptanceId=$TestId}
$source | ConvertTo-Json -Depth 5 | Set-Content ($package+'.build.json')
$package
