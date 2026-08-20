[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$package = (Resolve-Path -LiteralPath $PackagePath).Path
$trimCharacters = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$packageName = Split-Path -Leaf $package.TrimEnd($trimCharacters)
if ($packageName -cne 'metar-viewer-toolbar') {
    throw "The package directory must be named metar-viewer-toolbar; found '$packageName'."
}

$output = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $output
if ($outputDirectory) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("metar-viewer-package-" + [guid]::NewGuid().ToString('N'))
$stagedPackage = Join-Path $temporaryRoot $packageName

try {
    New-Item -ItemType Directory -Path $stagedPackage -Force | Out-Null
    Get-ChildItem -LiteralPath $package -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stagedPackage -Recurse -Force
    }

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $temporaryRoot,
        $output,
        [IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    Write-Host "Created Community package ZIP: $output"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
