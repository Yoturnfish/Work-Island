param([Parameter(Mandatory)][string]$PayloadDir,[string]$TestId='')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot
if(!$TestId){$TestId=[guid]::NewGuid().ToString('D').ToUpperInvariant()}
$id=[guid]::ParseExact($TestId,'D').ToString('D').ToUpperInvariant()
if($id -eq '6B22A7C8-B238-4372-93DF-B942E8E62435'){throw 'Use an independent installer identity'}
$root=Join-Path $project ('test-results\net48-installer-'+$id)
if(Test-Path $root){throw 'Preserving existing acceptance data'}
New-Item -ItemType Directory -Path $root|Out-Null
$app=Join-Path $root 'Application';$data=Join-Path $root 'Data';$exe=Join-Path $app 'WorkIsland.exe'
$records=[Collections.Generic.List[object]]::new();$live=$null
$dailyFile=Join-Path $env:LOCALAPPDATA 'WorkIsland\state.json';$daily=(Get-FileHash $dailyFile).Hash
function Save-Report {
    [ordered]@{passed=($records.Count-gt 0-and @($records|Where-Object {!$_.passed}).Count-eq 0);checks=@($records.ToArray());testRoot=$root;identity=$id;appHash=(Get-FileHash (Join-Path $PayloadDir 'WorkIsland.exe')).Hash;scope='Independent per-user installer GUID and data; no production installation changed'}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root 'report.json')
}
function Check([string]$Name,[bool]$Passed,$Evidence=$null){$records.Add(@{name=$Name;passed=$Passed;evidence=$Evidence});Save-Report;if(!$Passed){throw $Name}}
function Run([string]$File,[string[]]$Arguments){
    $info=[Diagnostics.ProcessStartInfo]::new($File);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $info.Environment['DOTNET_ROOT']=Join-Path $root 'no-modern-runtime';$info.Environment['DOTNET_ROOT_X64']=$info.Environment['DOTNET_ROOT'];$info.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
    $process=[Diagnostics.Process]::Start($info)
    try{$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();if(!$process.WaitForExit(60000)){throw 'Owned process wait expired; not terminated'};return @{code=$process.ExitCode;stdout=$stdout.GetAwaiter().GetResult();stderr=$stderr.GetAwaiter().GetResult()}}
    finally{$process.Dispose()}
}
function Invoke-CompatCli([string[]]$Arguments){$r=Run $exe $Arguments;return @{code=$r.code;response=($r.stdout|ConvertFrom-Json)}}
function Setup([string]$Label,[string[]]$Extra=@()){
    Run $package (@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/LOG='+$root+'\'+$Label+'.log'))+$Extra)
}
New-Item -ItemType Directory -Path $data|Out-Null
$now=[DateTimeOffset]::Now;$recordId=[guid]::NewGuid().ToString('D')
[ordered]@{schemaVersion=2;settings=@{goalSeconds=7200;paused=$true;hasCompletedOnboarding=$true;rules=@()};sessions=@(@{id=$recordId;applicationIdentifier='acceptance.exe';applicationName='Acceptance';categoryRawValue='other';dayKey=$now.ToString('yyyy-MM-dd');startedAt=$now.AddHours(-1).ToString('o');endedAt=$now.ToString('o');effectiveSeconds=3600})}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $data 'state.json')
$initial=(Get-FileHash (Join-Path $data 'state.json')).Hash
try{
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PayloadDir $PayloadDir -BuiltinFramework -TestRoot $root -TestId $id -OutputDir (Join-Path $root 'Packages') -PackageName WorkIsland-net48-acceptance|Out-Null
    $package=Join-Path $root 'Packages\WorkIsland-net48-acceptance.exe'
    $install=Setup 'install';Check 'isolated lightweight installer completes' ($install.code-eq 0) $install.code
    $manifest=Get-Content (Join-Path $PayloadDir 'FILES-SHA256.json') -Raw|ConvertFrom-Json
    $bad=@($manifest|Where-Object {(Get-FileHash (Join-Path $app $_.file)).Hash-ne $_.sha256})
    Check 'installed payload matches frozen binaries and contains no runtime' ($bad.Count-eq 0-and !(Test-Path (Join-Path $app 'runtime')))
    Check 'installation preserves pre-existing work data bytes' ((Get-FileHash (Join-Path $data 'state.json')).Hash-eq $initial)
    $launch=[Diagnostics.ProcessStartInfo]::new($exe);$launch.UseShellExecute=$false;$launch.CreateNoWindow=$true
    foreach($argument in @('--instance-review','--data-dir',$data)){$launch.ArgumentList.Add($argument)}
    $launch.Environment['DOTNET_ROOT']=Join-Path $root 'no-modern-runtime';$launch.Environment['DOTNET_ROOT_X64']=$launch.Environment['DOTNET_ROOT'];$launch.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
    $live=[Diagnostics.Process]::Start($launch);$started=$live.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
    $watch=[Diagnostics.Stopwatch]::StartNew();$query=$null
    do{$query=Invoke-CompatCli @('--query-instance','--data-dir',$data);if($query.code-eq 0-and $query.response.window.loaded){break};Start-Sleep -Milliseconds 100}while($watch.Elapsed.TotalSeconds-lt 12-and !$live.HasExited)
    Check 'installed app starts and hosts the verified lifecycle endpoint' ($query.code-eq 0-and $query.response.instance.processId-eq $live.Id)
    $modules=@($live.Modules|ForEach-Object {[ordered]@{name=$_.ModuleName;path=$_.FileName}})
    $modules|ConvertTo-Json|Set-Content (Join-Path $root 'loaded-modules.json')
    Check 'installed app loads Windows CLR and no modern coreclr' (@($modules|Where-Object {$_.name-eq 'clr.dll'-and $_.path.StartsWith($env:WINDIR+'\Microsoft.NET\',[StringComparison]::OrdinalIgnoreCase)}).Count-eq 1-and @($modules|Where-Object {$_.name-in @('coreclr.dll','System.Private.CoreLib.dll')}).Count-eq 0)
    $blocked=Setup 'blocked-repair';Check 'running installed app blocks repair' ($blocked.code-ne 0-and !$live.HasExited)
    $exit=Invoke-CompatCli @('--request-exit','--data-dir',$data,'--expect-pid',$live.Id.ToString(),'--expect-start-ticks',$started)
    Check 'installed app saves and exits normally' ($exit.code-eq 0-and $exit.response.status-eq 'exited'-and $live.HasExited-and $live.ExitCode-eq 0)
    $saved=Get-Content (Join-Path $data 'state.json') -Raw|ConvertFrom-Json
    Check 'normal exit retains paused state and exact session identity and duration' ($saved.settings.paused-and @($saved.sessions|Where-Object {$_.id-eq $recordId-and $_.effectiveSeconds-eq 3600}).Count-eq 1)
    $savedHash=(Get-FileHash (Join-Path $data 'state.json')).Hash
    $repair=Setup 'repair';Check 'stopped lightweight installation repairs without changing data' ($repair.code-eq 0-and (Get-FileHash (Join-Path $data 'state.json')).Hash-eq $savedHash)
    $failure=Setup 'rollback' @('/InjectFailure=1');Check 'injected upgrade failure restores the old app and preserves data' ($failure.code-ne 0-and (Get-FileHash $exe).Hash-eq (Get-FileHash (Join-Path $PayloadDir 'WorkIsland.exe')).Hash-and (Get-FileHash (Join-Path $data 'state.json')).Hash-eq $savedHash)
    $uninstall=Run (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+$root+'\uninstall.log'))
    Check 'isolated uninstall removes the program and retains work data' ($uninstall.code-eq 0-and !(Test-Path $exe)-and (Get-FileHash (Join-Path $data 'state.json')).Hash-eq $savedHash)
    Check 'daily work data remains unchanged throughout installation acceptance' ((Get-FileHash $dailyFile).Hash-eq $daily)
    Write-Output (Join-Path $root 'report.json')
}finally{
    if($live){
        if(!$live.HasExited){try{Invoke-CompatCli @('--request-exit','--data-dir',$data,'--expect-pid',$live.Id.ToString(),'--expect-start-ticks',$started)|Out-Null}catch{Write-Warning 'Owned test instance exit unverified; preserve it for inspection'}}
        $live.Dispose()
    }
    Save-Report
}
