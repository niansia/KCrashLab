[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)] [string] $ProfilePath,
    [Parameter(Mandatory)] [string] $CasePath,
    [Parameter(Mandatory)] [string] $MinimizedCasePath,
    [Parameter(Mandatory)] [string] $OutputPath,
    [Parameter(Mandatory)] [string] $WinDbgPath,
    [Parameter(Mandatory)] [string] $SymbolPath,
    [Parameter(Mandatory)] [pscredential] $GuestCredential,
    [string] $GuestAgentPath = 'C:\KCrashLab\KCrashLab.GuestAgent.exe',
    [string] $GuestDriverPath = 'C:\Windows\System32\drivers\KCrashLabTarget.sys',
    [string] $GuestCasePath = 'C:\KCrashLab\current.case.json',
    [string] $GuestJournalPath = 'C:\KCrashLab\attempt.jsonl',
    [string] $GuestDumpPath = 'C:\Windows\MEMORY.DMP',
    [int] $CrashTimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-KclCli([string[]] $Arguments) {
    & dotnet run --project (Join-Path $PSScriptRoot '..\src\KCrashLab.Cli') --configuration Release -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "KCrashLab CLI failed with exit code $LASTEXITCODE." }
}

function Get-IdentityHash([string] $Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-TreeHash([string] $Root) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $lines = foreach ($file in Get-ChildItem -LiteralPath $rootFull -File -Recurse | Sort-Object FullName) {
        $relative = $file.FullName.Substring($rootFull.Length).TrimStart('\').Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
    if (@($lines).Count -eq 0) { throw 'SymbolPath does not contain any files.' }
    Get-IdentityHash ($lines -join "`n")
}

function Wait-KclHeartbeat([guid] $VmId, [int] $TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $targetVm = Get-VM -Id $VmId
        $heartbeat = Get-VMIntegrationService -VM $targetVm -Name 'Heartbeat'
        if ($heartbeat.Enabled -and $heartbeat.PrimaryStatusDescription -eq 'OK') { return }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Guest heartbeat did not become healthy before the timeout.'
}

function Invoke-Guest([guid] $VmId, [scriptblock] $Script, [object[]] $Arguments = @()) {
    Invoke-Command -VMId $VmId -Credential $GuestCredential -ScriptBlock $Script -ArgumentList $Arguments
}

function Get-GuestBootTime([guid] $VmId) {
    Invoke-Guest $VmId { (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('O') }
}

function Get-GuestBuild([guid] $VmId) {
    Invoke-Guest $VmId {
        $os = Get-ComputerInfo
        '{0}|{1}|{2}' -f $os.OsName, $os.OsVersion, $os.WindowsBuildLabEx
    }
}

function Wait-DumpStable([guid] $VmId, [int] $StableSeconds, [int] $TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastLength = -1L
    $unchangedSince = [DateTimeOffset]::UtcNow
    do {
        try {
            $sample = Invoke-Guest $VmId {
                param($Path)
                if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @{ Length = $null; Exclusive = $false } }
                $item = Get-Item -LiteralPath $Path
                $exclusive = $false
                try {
                    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'None')
                    $exclusive = $true
                    $stream.Dispose()
                } catch { $exclusive = $false }
                @{ Length = $item.Length; Exclusive = $exclusive }
            } @($GuestDumpPath)
            if ($null -ne $sample.Length -and $sample.Length -gt 0) {
                if ($sample.Length -ne $lastLength) { $lastLength = $sample.Length; $unchangedSince = [DateTimeOffset]::UtcNow }
                elseif ($sample.Exclusive -and ([DateTimeOffset]::UtcNow - $unchangedSince).TotalSeconds -ge $StableSeconds) { return $sample.Length }
            }
        } catch { }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'MEMORY.DMP did not become stable and exclusively readable before the timeout.'
}

function Copy-FromGuest([guid] $VmId, [string] $Source, [string] $Destination) {
    $session = New-PSSession -VMId $VmId -Credential $GuestCredential
    try { Copy-Item -FromSession $session -LiteralPath $Source -Destination $Destination -Force }
    finally { Remove-PSSession $session }
}

$profileFull = (Resolve-Path -LiteralPath $ProfilePath).Path
$caseFull = (Resolve-Path -LiteralPath $CasePath).Path
$minimizedFull = (Resolve-Path -LiteralPath $MinimizedCasePath).Path
$windbgFull = (Resolve-Path -LiteralPath $WinDbgPath).Path
$symbolFull = (Resolve-Path -LiteralPath $SymbolPath).Path
$symbolsHash = Get-TreeHash $symbolFull
$outputFull = [IO.Path]::GetFullPath($OutputPath)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
if ($profileFull.StartsWith($repositoryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The private real-lab profile must be stored outside the repository.' }
if ($outputFull.TrimEnd('\').StartsWith($repositoryRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $outputFull.TrimEnd('\').Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Private G1 acquisition output must be stored outside the repository.'
}
if (Test-Path -LiteralPath $outputFull) {
    if ((Get-ChildItem -LiteralPath $outputFull -Force | Select-Object -First 1)) { throw 'Output directory must be empty.' }
} else { New-Item -ItemType Directory -Path $outputFull | Out-Null }
$gitDirty = & git -C $repositoryRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $gitDirty) { throw 'Track B requires a clean committed Git source tree.' }

Invoke-KclCli @('lab', 'validate-profile', $profileFull)
$originalCaseId = (& dotnet run --project (Join-Path $PSScriptRoot '..\src\KCrashLab.Cli') --configuration Release -- case id $caseFull | Select-Object -Last 1).Trim()
$minimizedCaseId = (& dotnet run --project (Join-Path $PSScriptRoot '..\src\KCrashLab.Cli') --configuration Release -- case id $minimizedFull | Select-Object -Last 1).Trim()
if ($originalCaseId -notmatch '^[0-9a-f]{64}$' -or $minimizedCaseId -notmatch '^[0-9a-f]{64}$') { throw 'Case identity calculation failed.' }
$profile = Get-Content -Raw -LiteralPath $profileFull | ConvertFrom-Json
$exchangeRoot = [IO.Path]::GetFullPath([string]$profile.exchange_root).TrimEnd('\')
if (-not $outputFull.TrimEnd('\').StartsWith($exchangeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must be a child of the validated exchange_root.'
}
$vmId = [guid]$profile.vm_id
$checkpointId = [guid]$profile.checkpoint_id
$vm = Get-VM -Id $vmId
$snapshot = Get-VMSnapshot -VM $vm | Where-Object Id -eq $checkpointId
if ($null -eq $snapshot -or @($snapshot).Count -ne 1) { throw 'Pinned checkpoint identity was not found exactly once.' }
$unsafeAdapters = foreach ($adapter in Get-VMNetworkAdapter -VM $vm) {
    if ([string]::IsNullOrWhiteSpace($adapter.SwitchName)) { continue }
    $switch = Get-VMSwitch -Name $adapter.SwitchName
    if ($switch.SwitchType -ne 'Private') { $adapter }
}
if (@($unsafeAdapters).Count -ne 0) { throw 'VM has a network adapter connected to a non-private Hyper-V switch.' }
$guestService = Get-VMIntegrationService -VM $vm -Name 'Guest Service Interface'
if (-not $guestService.Enabled) { throw 'Hyper-V Guest Service Interface must be enabled for hash-pinned case transfer.' }
if (-not $PSCmdlet.ShouldProcess("VM $vmId checkpoint $checkpointId", 'Run one discovery and three destructive cold replay attempts')) { return }

$driverHash = $profile.driver_sha256
$vmHash = Get-IdentityHash $profile.vm_id
$checkpointHash = Get-IdentityHash $profile.checkpoint_id
$authorizationHash = Get-IdentityHash $profile.authorization_id
$replays = @()
$discoveryDumpHash = $null
$discoveryRaw = $null
$discoveryRawHash = $null
$discoveryJournalHash = $null
$discoveryWatchdogHash = $null
$discoverySignature = $null

try {
    foreach ($attempt in 0..3) {
        $label = if ($attempt -eq 0) { 'discovery' } else { "replay-{0:D2}" -f $attempt }
        $attemptRoot = Join-Path $outputFull ("private-$label")
        $dispatchCase = if ($attempt -eq 0) { $caseFull } else { $minimizedFull }
        New-Item -ItemType Directory -Path $attemptRoot | Out-Null
        $watchdogPath = Join-Path $attemptRoot 'watchdog.jsonl'
        Restore-VMSnapshot -VMSnapshot $snapshot -Confirm:$false
        Start-VM -VM $vm | Out-Null
        Wait-KclHeartbeat $vmId $profile.heartbeat_timeout_seconds
        $observedGuestBuild = Get-GuestBuild $vmId
        if ($observedGuestBuild -ne $profile.guest_build) { throw "Pinned guest build mismatch: observed '$observedGuestBuild'." }
        $guestGate = Invoke-Guest $vmId {
            param($DriverPath)
            $service = Get-CimInstance Win32_SystemDriver -Filter "Name='KCrashLabTarget'"
            $dumpPolicy = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl').CrashDumpEnabled
            $verifier = (& verifier.exe /querysettings 2>&1 | Out-String)
            @{
                DriverState = $service.State
                DriverHash = (Get-FileHash -LiteralPath $DriverPath -Algorithm SHA256).Hash.ToLowerInvariant()
                DumpPolicy = $dumpPolicy
                DumpFreeBytes = (Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace
                VerifierTargetsDriver = $verifier.Contains('KCrashLabTarget.sys', [StringComparison]::OrdinalIgnoreCase)
            }
        } @($GuestDriverPath)
        if ($guestGate.DriverState -ne 'Running' -or $guestGate.DriverHash -ne $driverHash) { throw 'Loaded guest driver state/hash gate failed.' }
        if ($guestGate.DumpPolicy -notin @(1, 2)) { throw 'Guest must use complete or kernel memory dump policy.' }
        if ([long]$guestGate.DumpFreeBytes -lt ([long]$vm.MemoryAssigned + 1GB)) { throw 'Guest system volume does not have conservative dump free space.' }
        if (-not $guestGate.VerifierTargetsDriver) { throw 'Driver Verifier is not configured for KCrashLabTarget.sys.' }
        $bootBefore = Get-GuestBootTime $vmId
        Copy-VMFile -VM $vm -SourcePath $dispatchCase -DestinationPath $GuestCasePath -FileSource Host -CreateFullPath -Force
        Invoke-Guest $vmId {
            param($Agent, $Case, $Driver, $Hash, $Journal)
            Remove-Item -LiteralPath $Journal -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath $Agent -ArgumentList @('--case', $Case, '--driver', $Driver, '--expected-driver-sha256', $Hash, '--journal', $Journal)
        } @($GuestAgentPath, $GuestCasePath, $GuestDriverPath, $driverHash, $GuestJournalPath)

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($CrashTimeoutSeconds)
        $bootAfter = $bootBefore
        do {
            Start-Sleep -Seconds 3
            $vmSample = Get-VM -Id $vmId
            $heartbeatSample = Get-VMIntegrationService -VM $vmSample -Name 'Heartbeat'
            try { Wait-KclHeartbeat $vmId 5; $bootAfter = Get-GuestBootTime $vmId } catch { }
            [ordered]@{
                observed_at_utc = [DateTimeOffset]::UtcNow.ToString('O'); vm_state = [string]$vmSample.State
                heartbeat = [string]$heartbeatSample.PrimaryStatusDescription; boot_before = $bootBefore; boot_observed = $bootAfter
            } | ConvertTo-Json -Compress | Add-Content -LiteralPath $watchdogPath -Encoding utf8
            if ($bootAfter -ne $bootBefore) { break }
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($bootAfter -eq $bootBefore) { throw "$label did not observe a guest reboot." }

        Wait-KclHeartbeat $vmId $profile.heartbeat_timeout_seconds
        [void](Wait-DumpStable $vmId $profile.dump_stability_seconds $CrashTimeoutSeconds)
        $dumpPath = Join-Path $attemptRoot 'MEMORY.DMP'
        $journalPath = Join-Path $attemptRoot 'attempt.jsonl'
        Copy-FromGuest $vmId $GuestDumpPath $dumpPath
        Copy-FromGuest $vmId $GuestJournalPath $journalPath
        $expectedCaseId = if ($attempt -eq 0) { $originalCaseId } else { $minimizedCaseId }
        $journalEvents = @(Get-Content -LiteralPath $journalPath | ForEach-Object { $_ | ConvertFrom-Json })
        $lastDispatch = $journalEvents | Where-Object event_name -eq 'OPERATION_DISPATCHING' | Select-Object -Last 1
        if ($null -eq $lastDispatch -or $lastDispatch.case_id -ne $expectedCaseId -or $lastDispatch.operation -ne 'SUBMIT_RECORD') {
            throw "$label durable journal does not attribute the crash boundary to the expected case and operation."
        }
        $dumpHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dumpPath).Hash.ToLowerInvariant()
        $rawPath = Join-Path $attemptRoot 'windbg.raw.txt'
        & $windbgFull -y $symbolFull -z $dumpPath -c '!analyze -v; kv; q' 2>&1 | Out-File -LiteralPath $rawPath -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw "WinDbg failed for $label with exit code $LASTEXITCODE." }
        $analysisPath = Join-Path $attemptRoot 'analysis.json'
        $signature = (& dotnet run --project (Join-Path $PSScriptRoot '..\src\KCrashLab.Cli') --configuration Release -- lab triage $rawPath --output $analysisPath | Select-Object -Last 1).Trim()
        if ($LASTEXITCODE -ne 0 -or $signature -notmatch '^[0-9a-f]{64}$') { throw "Triage failed for $label." }
        if ($attempt -eq 0) {
            $discoveryDumpHash = $dumpHash; $discoveryRaw = $rawPath
            $discoveryRawHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $rawPath).Hash.ToLowerInvariant()
            $discoveryJournalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $journalPath).Hash.ToLowerInvariant()
            $discoveryWatchdogHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $watchdogPath).Hash.ToLowerInvariant()
            $discoverySignature = $signature
        } else {
            $classification = if ($signature -eq $discoverySignature) { 'MATCH' } else { 'DIVERGENT' }
            $journalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $journalPath).Hash.ToLowerInvariant()
            $watchdogHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $watchdogPath).Hash.ToLowerInvariant()
            $replays += [ordered]@{ attempt = $attempt; classification = $classification; signature = $signature; dump_sha256 = $dumpHash; journal_sha256 = $journalHash; watchdog_sha256 = $watchdogHash }
            if ($classification -ne 'MATCH') { throw "Replay $attempt produced a divergent signature." }
        }
    }
} finally {
    Restore-VMSnapshot -VMSnapshot $snapshot -Confirm:$false -ErrorAction Continue
}

$gitCommit = (& git -C (Join-Path $PSScriptRoot '..') rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-f]{40}$') { throw 'A committed Git source tree is required.' }
$acquisition = [ordered]@{
    schema_version = 1; execution_mode = 'REAL_LAB'; original_case_path = $caseFull; minimized_case_path = $minimizedFull
    driver_sha256 = $driverHash; vm_identity_sha256 = $vmHash; checkpoint_identity_sha256 = $checkpointHash; authorization_sha256 = $authorizationHash
    guest_build = $profile.guest_build; symbols_sha256 = $symbolsHash; dump_sha256 = $discoveryDumpHash; raw_windbg_sha256 = $discoveryRawHash
    discovery_journal_sha256 = $discoveryJournalHash; discovery_watchdog_sha256 = $discoveryWatchdogHash; raw_windbg_path = $discoveryRaw
    replays = $replays; recorded_at_utc = [DateTimeOffset]::UtcNow.ToString('O'); git_commit = $gitCommit
}
$acquisitionPath = Join-Path $outputFull 'private-acquisition.json'
$acquisition | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $acquisitionPath -Encoding utf8NoBOM
$publicBundle = Join-Path $outputFull 'public-evidence'
Invoke-KclCli @('lab', 'evidence', 'build', '--acquisition', $acquisitionPath, '--output', $publicBundle)
Write-Host "G1 PASS: three matching cold replays; public bundle: $publicBundle"
