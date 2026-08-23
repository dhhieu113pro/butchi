param(
  [Parameter(Mandatory = $true)][string]$WindowTitle,
  [Parameter(Mandatory = $true)][string]$OutputPath,
  [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ButchiWindowCapture {
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

  [DllImport("user32.dll", SetLastError = true)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
"@

Add-Type -AssemblyName System.Drawing

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$hwnd = [IntPtr]::Zero
while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
  $hwnd = [ButchiWindowCapture]::FindWindow($null, $WindowTitle)
  if ($hwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
}

if ($hwnd -eq [IntPtr]::Zero) {
  throw "Window '$WindowTitle' was not found within $TimeoutSeconds seconds."
}

$rect = New-Object ButchiWindowCapture+RECT
if (-not [ButchiWindowCapture]::GetWindowRect($hwnd, [ref]$rect)) {
  throw "Could not read bounds for '$WindowTitle'."
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 200 -or $height -lt 200) {
  throw "Implausible window bounds for '$WindowTitle': ${width}x${height}."
}

$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
  $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
  $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
  $graphics.Dispose()
  $bitmap.Dispose()
}

$file = Get-Item $OutputPath
if ($file.Length -lt 10000) {
  throw "Screenshot '$OutputPath' is unexpectedly small ($($file.Length) bytes)."
}

Write-Host "Captured '$WindowTitle' -> $OutputPath (${width}x${height}, $($file.Length) bytes)"
