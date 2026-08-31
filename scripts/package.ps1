[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$RecordedResultsOutputPath,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceOutput = [System.IO.Path]::GetFullPath($OutputPath)

function Assert-ZipTarget {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::GetExtension($Path) -ne '.zip') {
        throw "Package output must use the .zip extension: $Path"
    }

    $parent = [System.IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Package output has no parent directory: $Path"
    }

    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    if ([System.IO.File]::Exists($Path)) {
        if (-not $Force) {
            throw "Package output already exists; pass -Force to replace this exact file: $Path"
        }

        Remove-Item -LiteralPath $Path -Force
    }
}

function Assert-CleanArchive {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $banned = $archive.Entries | Where-Object {
            $_.FullName -match '(^|/)(bin|obj|\.git|artifacts|TestResults)(/|$)' -or
            $_.FullName -match '\.(dll|pdb|so|dylib|dmp)$'
        }
        if ($banned) {
            $names = ($banned | Select-Object -First 10 -ExpandProperty FullName) -join ', '
            throw "Package contains forbidden build/runtime files: $names"
        }
    }
    finally {
        $archive.Dispose()
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    git rev-parse --verify HEAD *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'A Git commit is required before creating a reviewer package.'
    }

    $status = @(git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        throw 'Reviewer packages require a clean Git working tree.'
    }

    Assert-ZipTarget -Path $sourceOutput
    git archive --format=zip "--output=$sourceOutput" HEAD
    if ($LASTEXITCODE -ne 0) {
        throw 'git archive failed.'
    }

    Assert-CleanArchive -Path $sourceOutput

    if (-not [string]::IsNullOrWhiteSpace($RecordedResultsOutputPath)) {
        $resultsOutput = [System.IO.Path]::GetFullPath($RecordedResultsOutputPath)
        Assert-ZipTarget -Path $resultsOutput
        $recordedRoot = Join-Path $repositoryRoot 'results/recorded'
        if (-not (Test-Path -LiteralPath $recordedRoot -PathType Container)) {
            throw 'results/recorded does not exist.'
        }

        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $recordedRoot,
            $resultsOutput,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
    }
}
finally {
    Pop-Location
}

Write-Output "Source package: $sourceOutput"
if (-not [string]::IsNullOrWhiteSpace($RecordedResultsOutputPath)) {
    Write-Output "Recorded-results package: $([System.IO.Path]::GetFullPath($RecordedResultsOutputPath))"
}
