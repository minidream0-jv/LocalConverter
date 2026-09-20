param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$ZigPath
)

$ErrorActionPreference = 'Stop'
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

if ([string]::IsNullOrWhiteSpace($ZigPath)) {
    $ZigPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\.tools\zig\zig.exe'))
}

if (-not (Test-Path -LiteralPath $ZigPath)) {
    throw "Zig compiler not found: $ZigPath"
}

$compilerArguments = @(
    'c++',
    '-target', 'x86_64-windows-gnu',
    '-std=c++20',
    '-O2',
    '-Wno-nullability-completeness',
    '-Wno-dll-attribute-on-redeclaration',
    '-DUNICODE', '-D_UNICODE',
    '-shared',
    "$PSScriptRoot\ShellExtension.cpp",
    "$PSScriptRoot\ShellExtension.def",
    '-lole32', '-lshell32', '-lshlwapi', '-luuid',
    '-Wl,--subsystem,windows',
    '-o', "$output\LocalConverter.ShellExtension.dll"
)

& $ZigPath $compilerArguments

if ($LASTEXITCODE -ne 0) {
    throw "Shell extension compilation failed with exit code $LASTEXITCODE."
}
