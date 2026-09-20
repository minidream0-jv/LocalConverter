param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0.0',
    [string]$CertificatePath,
    [string]$CertificatePassword = 'LocalConverterBuild'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$stageRoot = Join-Path $artifactsRoot 'package'
$publishRoot = Join-Path $artifactsRoot 'publish'
$setupRoot = Join-Path $artifactsRoot 'setup'
$packagePath = Join-Path $artifactsRoot 'LocalConverter.msix'
$publicCertificatePath = Join-Path $artifactsRoot 'LocalConverter.cer'

function Assert-SafeBuildPath([string]$Path) {
    $resolvedRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe build path outside repository: $resolvedPath"
    }
}

function Reset-BuildDirectory([string]$Path) {
    Assert-SafeBuildPath $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Get-DotNet {
    $local = Join-Path $toolsRoot 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed) {
        $sdks = & $installed.Source --list-sdks
        if ($sdks -match '^10\.') { return $installed.Source }
    }

    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
    $installer = Join-Path $toolsRoot 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    $installDirectory = Join-Path $toolsRoot 'dotnet'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 10.0 -InstallDir $installDirectory -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'Unable to install the .NET SDK.' }
    return (Join-Path $installDirectory 'dotnet.exe')
}

function Get-Zig {
    $local = Join-Path $toolsRoot 'zig\zig.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    $installed = Get-Command zig -ErrorAction SilentlyContinue
    if ($installed) { return $installed.Source }

    $index = Invoke-RestMethod 'https://ziglang.org/download/index.json'
    $zigVersion = $index.PSObject.Properties.Name | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | Select-Object -First 1
    if (-not $zigVersion) { throw 'Unable to determine the current Zig version.' }
    $archive = Join-Path $toolsRoot "zig-$zigVersion.zip"
    $extract = Join-Path $toolsRoot "zig-extract-$zigVersion"
    $destination = Join-Path $toolsRoot "zig-$zigVersion"
    Invoke-WebRequest $index.$zigVersion.'x86_64-windows'.tarball -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $extract
    $source = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
    Move-Item -LiteralPath $source.FullName -Destination $destination
    New-Item -ItemType Junction -Path (Join-Path $toolsRoot 'zig') -Target $destination | Out-Null
    return (Join-Path $destination 'zig.exe')
}

function Get-Ffmpeg {
    $ffmpeg = Join-Path $toolsRoot 'ffmpeg\ffmpeg.exe'
    if (Test-Path -LiteralPath $ffmpeg) { return $ffmpeg }
    $archive = Join-Path $toolsRoot 'ffmpeg-gpl-shared.zip'
    $extract = Join-Path $toolsRoot 'ffmpeg-extract'
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -L --fail --retry 3 --output $archive 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
        if ($LASTEXITCODE -ne 0) { throw 'Unable to download FFmpeg.' }
    }
    else {
        Invoke-WebRequest 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip' -OutFile $archive
    }
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $extract
    $downloaded = Get-ChildItem -LiteralPath $extract -Filter 'ffmpeg.exe' -Recurse | Select-Object -First 1
    if (-not $downloaded) { throw 'The FFmpeg archive did not contain ffmpeg.exe.' }
    $destination = Split-Path -Parent $ffmpeg
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $downloaded.Directory.FullName '*') -Destination $destination -Recurse -Force
    $license = Get-ChildItem -LiteralPath $extract -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|COPYING)' } | Select-Object -First 1
    if ($license) { Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $destination 'FFmpeg-LICENSE.txt') }
    return $ffmpeg
}

function New-Logo([string]$Path, [int]$Size) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(0, 103, 192))
        $font = [System.Drawing.Font]::new('Segoe UI', [Math]::Max(10, $Size * 0.48), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $brush = [System.Drawing.Brushes]::White
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString('C', $font, $brush, [System.Drawing.RectangleF]::new(0, 0, $Size, $Size), $format)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        $font.Dispose()
        $format.Dispose()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
Reset-BuildDirectory $artifactsRoot
New-Item -ItemType Directory -Force -Path $stageRoot, $publishRoot, $setupRoot | Out-Null

$dotnet = Get-DotNet
$zig = Get-Zig
$ffmpeg = Get-Ffmpeg

Write-Host 'Restoring and testing managed projects...'
& $dotnet restore (Join-Path $repoRoot 'LocalConverter.slnx')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
& $dotnet test (Join-Path $repoRoot 'tests\Conversion.Tests\Conversion.Tests.csproj') -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

Write-Host 'Publishing the background worker...'
& $dotnet publish (Join-Path $repoRoot 'src\Converter.App\Converter.App.csproj') -c $Configuration -r win-x64 --self-contained true -o $publishRoot --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
Copy-Item -Path (Join-Path $publishRoot '*') -Destination $stageRoot -Recurse -Force

Write-Host 'Building the native Explorer command...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'src\ShellExtension\build.ps1') -OutputDirectory $stageRoot -ZigPath $zig
if ($LASTEXITCODE -ne 0) { throw 'Native shell extension build failed.' }

$ffmpegStage = Join-Path $stageRoot 'tools'
New-Item -ItemType Directory -Force -Path $ffmpegStage | Out-Null
$ffmpegCache = Split-Path -Parent $ffmpeg
Copy-Item -LiteralPath $ffmpeg -Destination (Join-Path $ffmpegStage 'ffmpeg.exe')
Get-ChildItem -LiteralPath $ffmpegCache -Filter '*.dll' | Copy-Item -Destination $ffmpegStage
$ffmpegLicense = Join-Path (Split-Path -Parent $ffmpeg) 'FFmpeg-LICENSE.txt'
if (Test-Path -LiteralPath $ffmpegLicense) { Copy-Item -LiteralPath $ffmpegLicense -Destination $ffmpegStage }
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $stageRoot

$assets = Join-Path $stageRoot 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
New-Logo (Join-Path $assets 'Square44x44Logo.png') 44
New-Logo (Join-Path $assets 'Square150x150Logo.png') 150
New-Logo (Join-Path $assets 'StoreLogo.png') 50

Write-Host 'Restoring Windows SDK packaging tools...'
$packageCache = Join-Path $toolsRoot 'nuget-packages'
& $dotnet restore (Join-Path $repoRoot 'scripts\PackagingTools.csproj') --packages $packageCache
if ($LASTEXITCODE -ne 0) { throw 'Windows SDK tools restore failed.' }
$makeAppx = Get-ChildItem -LiteralPath $packageCache -Filter 'makeappx.exe' -Recurse | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
$signTool = Get-ChildItem -LiteralPath $packageCache -Filter 'signtool.exe' -Recurse | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
if (-not $makeAppx -or -not $signTool) { throw 'MakeAppx.exe or SignTool.exe was not found.' }

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    Write-Host 'Creating a per-user development signing certificate...'
    $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=LocalConverterDevelopment' -KeyUsage DigitalSignature -FriendlyName 'Local Converter development package' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
    $CertificatePath = Join-Path $artifactsRoot 'LocalConverter.Development.pfx'
    $securePassword = ConvertTo-SecureString -String $CertificatePassword -AsPlainText -Force
    Export-PfxCertificate -Cert $certificate -FilePath $CertificatePath -Password $securePassword | Out-Null
    Export-Certificate -Cert $certificate -FilePath $publicCertificatePath | Out-Null
    $publisher = $certificate.Subject
}
else {
    $CertificatePath = [System.IO.Path]::GetFullPath($CertificatePath)
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath, $CertificatePassword, $flags)
    [System.IO.File]::WriteAllBytes($publicCertificatePath, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $publisher = $certificate.Subject
}

$manifestText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'packaging\AppxManifest.xml')
$manifestText = $manifestText.Replace('CN=LocalConverterDevelopment', $publisher).Replace('Version="1.0.0.0"', "Version=`"$Version`"")
$manifestPath = Join-Path $stageRoot 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifestPath, $manifestText, [System.Text.UTF8Encoding]::new($false))

Write-Host 'Packing and signing MSIX...'
& $makeAppx.FullName pack /d $stageRoot /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed.' }
$signArguments = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
if ($CertificatePassword.Length -gt 0) { $signArguments += @('/p', $CertificatePassword) }
$signArguments += $packagePath
& $signTool.FullName $signArguments
if ($LASTEXITCODE -ne 0) { throw 'Package signing failed.' }

Write-Host 'Publishing the graphical installer...'
& $dotnet publish (Join-Path $repoRoot 'src\Converter.Setup\Converter.Setup.csproj') -c $Configuration -r win-x64 --self-contained true -o $setupRoot --no-restore "/p:InstallerPackagePath=$packagePath" "/p:InstallerCertificatePath=$publicCertificatePath"
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
$setupArtifact = Join-Path $artifactsRoot 'LocalConverter.Setup.exe'
Copy-Item -LiteralPath (Join-Path $setupRoot 'LocalConverter.Setup.exe') -Destination $setupArtifact -Force
$setupSignArguments = @($signArguments[0..($signArguments.Count - 2)]) + $setupArtifact
& $signTool.FullName $setupSignArguments
if ($LASTEXITCODE -ne 0) { throw 'Installer signing failed.' }

Write-Host "Build complete: $artifactsRoot" -ForegroundColor Green
