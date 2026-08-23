param(
  [Parameter(Mandatory = $true)][string]$WindowTitle,
  [Parameter(Mandatory = $true)][string]$OutputPath,
  [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ButchiCaptureNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$handle = [IntPtr]::Zero
while ((Get-Date) -lt $deadline) {
  $handle = [ButchiCaptureNative]::FindWindow($null, $WindowTitle)
  if ($handle -ne [IntPtr]::Zero) { break }
  Start-Sleep -Milliseconds 250
}
if ($handle -eq [IntPtr]::Zero) { throw "Window not found: $WindowTitle" }

$rect = New-Object ButchiCaptureNative+RECT
if (-not [ButchiCaptureNative]::GetWindowRect($handle, [ref]$rect)) { throw "Could not read window bounds: $WindowTitle" }
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 200 -or $height -lt 200) { throw "Implausible window bounds: ${width}x${height}" }

[void][ButchiCaptureNative]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 700
$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
  $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
  $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
  $graphics.Dispose()
  $bitmap.Dispose()
}
if (-not (Test-Path $OutputPath)) { throw "Capture was not created: $OutputPath" }
if ((Get-Item $OutputPath).Length -lt 10000) { throw "Capture is unexpectedly small: $OutputPath" }
Write-Host "Captured $WindowTitle -> $OutputPath (${width}x${height})"
