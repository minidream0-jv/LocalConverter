param(
    [Parameter(Mandatory = $true)]
    [string]$ImagePath,
    [string]$ZigPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ZigPath)) {
    $ZigPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\.tools\zig\zig.exe'))
}

$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$resolvedImage = [System.IO.Path]::GetFullPath($ImagePath)
$imageDirectory = [System.IO.Path]::GetDirectoryName($resolvedImage)
$imageStem = [System.IO.Path]::GetFileNameWithoutExtension($resolvedImage)
$candidatePattern = '^' + [Regex]::Escape($imageStem) + '(?: \(\d+\))?$'
$existingOutputs = @(Get-ChildItem -LiteralPath $imageDirectory -File | Where-Object {
    $_.Extension -ieq '.webp' -and $_.BaseName -match $candidatePattern
} | ForEach-Object FullName)
$arguments = @(
    'c++', '-target', 'x86_64-windows-gnu', '-std=c++20', '-O2',
    '-Wno-nullability-completeness', '-DUNICODE', '-D_UNICODE', '-municode',
    (Join-Path $PSScriptRoot 'ShellInvokeTest.cpp'),
    '-lole32', '-lshell32', '-luuid',
    '-o', (Join-Path $output 'ShellInvokeTest.exe')
)
& $ZigPath $arguments
if ($LASTEXITCODE -ne 0) { throw 'Shell integration test compilation failed.' }

& (Join-Path $output 'ShellInvokeTest.exe') $resolvedImage
if ($LASTEXITCODE -ne 0) { throw "Shell command invocation failed with exit code $LASTEXITCODE." }

$deadline = [DateTime]::UtcNow.AddSeconds(30)
do {
    $created = Get-ChildItem -LiteralPath $imageDirectory -File | Where-Object {
        $_.Extension -ieq '.webp' -and $_.BaseName -match $candidatePattern -and
        $_.FullName -notin $existingOutputs -and $_.Length -gt 0
    } | Select-Object -First 1
    if (-not $created) { Start-Sleep -Milliseconds 250 }
} while (-not $created -and [DateTime]::UtcNow -lt $deadline)

if (-not $created) { throw 'The shell command did not produce a non-empty WEBP file within 30 seconds.' }
Write-Output "Created: $($created.FullName)"
