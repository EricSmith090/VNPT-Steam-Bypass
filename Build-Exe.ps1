#requires -Version 5.1
<#
.SYNOPSIS
    Build the standalone VNPT Steam Bypass GUI executable.

.DESCRIPTION
    Compiles src\SteamBypassGui.cs and src\UiControls.cs with the .NET Framework
    C# compiler. The four PowerShell scripts are embedded into the EXE, so the
    result is a single self-contained file.

.PARAMETER NoIcon
    Do not regenerate src\app.ico.

.EXAMPLE
    .\Build-Exe.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoIcon
)

$ErrorActionPreference = 'Stop'

function New-AppIcon {
    param([string]$Path)

    Add-Type -AssemblyName System.Drawing

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $images = New-Object System.Collections.Generic.List[object]

    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size, $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([System.Drawing.Color]::Transparent)

            $bounds = [System.Drawing.RectangleF]::new(
                ($size * 0.04), ($size * 0.04), ($size * 0.92), ($size * 0.92))

            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $bounds,
                [System.Drawing.Color]::FromArgb(56, 189, 248),
                [System.Drawing.Color]::FromArgb(37, 99, 235),
                45.0)

            try {
                $graphics.FillEllipse($gradient, $bounds)
            }
            finally {
                $gradient.Dispose()
            }

            $ring = [System.Drawing.Pen]::new(
                [System.Drawing.Color]::FromArgb(95, 255, 255, 255),
                [Math]::Max(1.0, $size * 0.014))

            try {
                $graphics.DrawEllipse($ring, $bounds)
            }
            finally {
                $ring.Dispose()
            }

            $bolt = @(
                [System.Drawing.PointF]::new(($size * 0.57), ($size * 0.14)),
                [System.Drawing.PointF]::new(($size * 0.25), ($size * 0.55)),
                [System.Drawing.PointF]::new(($size * 0.47), ($size * 0.55)),
                [System.Drawing.PointF]::new(($size * 0.41), ($size * 0.86)),
                [System.Drawing.PointF]::new(($size * 0.75), ($size * 0.44)),
                [System.Drawing.PointF]::new(($size * 0.53), ($size * 0.44))
            )

            $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            try {
                $graphics.FillPolygon($white, $bolt)
            }
            finally {
                $white.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add([pscustomobject]@{
                Size = $size
                Data = $stream.ToArray()
            })
        }
        finally {
            $stream.Dispose()
            $bitmap.Dispose()
        }
    }

    $file = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($file)

    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$images.Count)

        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$image.Data.Length)
            $writer.Write([UInt32]$offset)
            $offset += $image.Data.Length
        }

        foreach ($image in $images) {
            $writer.Write($image.Data)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

$root = $PSScriptRoot
Push-Location $root

try {
    $cscCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    $csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if (-not $csc) {
        throw 'The .NET Framework C# compiler (csc.exe) was not found. Install the .NET Framework 4.x developer pack.'
    }

    $sourceDir = Join-Path $root 'src'
    $distDir = Join-Path $root 'dist'
    $manifest = Join-Path $sourceDir 'app.manifest'
    $icon = Join-Path $sourceDir 'app.ico'

    foreach ($required in @(
        (Join-Path $sourceDir 'SteamBypassGui.cs'),
        (Join-Path $sourceDir 'UiControls.cs'),
        $manifest,
        (Join-Path $root 'SteamBypass.Common.ps1'),
        (Join-Path $root 'Enable-SteamBypass.ps1'),
        (Join-Path $root 'Disable-SteamBypass.ps1'),
        (Join-Path $root 'Test-SteamBypass.ps1')
    )) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Required file is missing: $required"
        }
    }

    if (-not (Test-Path -LiteralPath $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }

    if (-not $NoIcon -and -not (Test-Path -LiteralPath $icon)) {
        Write-Host 'Generating application icon...' -ForegroundColor Gray
        New-AppIcon -Path $icon
    }

    $output = Join-Path $distDir 'VNPT-Steam-Bypass.exe'

    $cscArguments = @(
        '/nologo',
        '/target:winexe',
        '/platform:anycpu',
        '/optimize+',
        '/debug-',
        ('/out:' + $output),
        ('/win32manifest:' + $manifest),
        '/reference:System.dll',
        '/reference:System.Core.dll',
        '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll',
        'src\SteamBypassGui.cs',
        'src\UiControls.cs',
        ('/resource:SteamBypass.Common.ps1,Scripts.SteamBypass.Common.ps1'),
        ('/resource:Enable-SteamBypass.ps1,Scripts.Enable-SteamBypass.ps1'),
        ('/resource:Disable-SteamBypass.ps1,Scripts.Disable-SteamBypass.ps1'),
        ('/resource:Test-SteamBypass.ps1,Scripts.Test-SteamBypass.ps1')
    )

    if (Test-Path -LiteralPath $icon) {
        $cscArguments += ('/win32icon:' + $icon)
    }

    Write-Host 'Compiling...' -ForegroundColor Cyan
    & $csc @cscArguments
    if ($LASTEXITCODE -ne 0) {
        throw "csc.exe failed with exit code $LASTEXITCODE."
    }

    $file = Get-Item -LiteralPath $output
    Write-Host ''
    Write-Host ('Built: {0}' -f $file.FullName) -ForegroundColor Green
    Write-Host ('Size:  {0:N0} bytes' -f $file.Length) -ForegroundColor Gray
    Write-Host ''
    Write-Host 'Double-click the EXE and accept the UAC prompt.' -ForegroundColor Gray
}
finally {
    Pop-Location
}
