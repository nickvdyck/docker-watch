#!/usr/bin/env pwsh
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Debug', 'Release')]
    $Configuration = $null,
	[switch]
	$IsOfficialBuild
)

Set-StrictMode -Version 1
$ErrorActionPreference = 'Stop'

function exec([string]$_cmd) {
    Write-Host ">>> $_cmd $args" -ForegroundColor Cyan
    $ErrorActionPreference = 'Continue'
    & $_cmd @args
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed with exit code $LASTEXITCODE"
        exit 1
    }
}

#
# Main
#
if (!$Configuration) {
    $Configuration = if ($env:CI -or $IsOfficialBuild) { 'Release' } else { 'Debug' }
}

[string[]] $MSBuildArgs = @("-p:Configuration=$Configuration")

if ($IsOfficialBuild) {
	$MSBuildArgs += '-p:CI=true'
}

$artifacts = "$PSScriptRoot/../artifacts/"

Remove-Item -Recurse $artifacts -ErrorAction Ignore

exec dotnet build @MSBuildArgs

exec dotnet pack `
    --no-build `
    -o $artifacts @MSBuildArgs

exec dotnet publish `
    -r win-x64 @MSBuildArgs

exec dotnet publish `
    -r osx-x64 @MSBuildArgs

Write-Host 'Done' -ForegroundColor Magenta
