<#
.SYNOPSIS
    Builds the CLI zip for a release and, with -Publish, tags it and creates the GitHub pre-release.

.DESCRIPTION
    Runs on a machine with the Empower Toolkit (see README.md): the CLI can't be built without its interop, which CI
    doesn't have. Never ships a Waters file: the build fails if one ends up in the output.

    1. Checks the working tree is clean and on main (with -Publish).
    2. Runs the Core tests.
    3. Builds the CLI in Release, versioned from -Version.
    4. Fails if any Empower Toolkit file is in the output.
    5. Zips the output with README.md and LICENCE.txt into artifacts\empower2asm-<version>.zip.
    6. With -Publish: tags v<version>, pushes the tag and creates the GitHub pre-release with the zip.

.EXAMPLE
    .\build\release.ps1 -Version 0.1.0            # build and check the zip only
    .\build\release.ps1 -Version 0.1.0 -Publish   # then tag and publish the pre-release
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Checked([string] $what, [scriptblock] $command) {
    Write-Host "== $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

if (git status --porcelain) { throw 'The working tree has uncommitted changes: commit or stash them first.' }
if ($Publish) {
    $branch = git rev-parse --abbrev-ref HEAD
    if ($branch -ne 'main') { throw "Publish from main (currently on '$branch')." }
    if (git tag --list "v$Version") { throw "Tag v$Version already exists." }
}

Invoke-Checked 'Core tests' { dotnet test Empower2Asm.Core.slnf --configuration Release --nologo }

$output = Join-Path $root 'src\Empower2Asm.Cli\bin\Release\net48'
if (Test-Path $output) { Remove-Item -Recurse -Force $output }
Invoke-Checked 'CLI build' { dotnet build src\Empower2Asm.Cli --configuration Release --nologo "-p:Version=$Version" }

# The Toolkit is commercial software: its interop types are embedded, and no Waters file may be shipped.
$waters = Get-ChildItem $output -Recurse | Where-Object { $_.Name -match '^(Interop\.|MilTk|Millennium|Waters)' }
if ($waters) { throw "Empower Toolkit files in the output, not shipped: $($waters.Name -join ', ')" }

$staging = Join-Path $root "artifacts\empower2asm-$Version"
$zip = "$staging.zip"
foreach ($path in $staging, $zip) { if (Test-Path $path) { Remove-Item -Recurse -Force $path } }
New-Item -ItemType Directory -Force $staging | Out-Null
Get-ChildItem $output | Where-Object { $_.Extension -ne '.pdb' } | Copy-Item -Destination $staging -Recurse
Copy-Item README.md, LICENCE.txt -Destination $staging
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $staging
Write-Host "== $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"

if (-not $Publish) {
    Write-Host 'Not published: run again with -Publish to tag and create the GitHub pre-release.'
    return
}

Invoke-Checked "Tag v$Version" { git tag -a "v$Version" -m "empower2asm $Version" }
Invoke-Checked 'Push tag' { git push origin "v$Version" }
Invoke-Checked 'GitHub pre-release' { gh release create "v$Version" $zip --prerelease --title "empower2asm $Version" --generate-notes }
