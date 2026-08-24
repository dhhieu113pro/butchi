param(
  [Parameter(Mandatory = $true)][string]$WindowTitle,
  [Parameter(Mandatory = $true)][string]$OutputPath,
  [int]$TimeoutSeconds = 45,
  [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class ButchiWindowCapture {
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

  [DllImport("user32.dll", SetLastError = true)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [DllImport("user32.dll")]
  public static extern bool IsWindowVisible(IntPtr hWnd);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

  [DllImport("user32.dll")]
  public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

  [DllImport("user32.dll")]
  public static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
"@

Add-Type -AssemblyName System.Drawing

function Get-VisibleWindowTitles {
  $titles = New-Object System.Collections.Generic.List[string]
  $callback = [ButchiWindowCapture+EnumWindowsProc]{
    param([IntPtr]$hWnd, [IntPtr]$lParam)
    if (-not [ButchiWindowCapture]::IsWindowVisible($hWnd)) { return $true }
    $sb = New-Object System.Text.StringBuilder 512
    [void][ButchiWindowCapture]::GetWindowText($hWnd, $sb, $sb.Capacity)
    $title = $sb.ToString()
    if (-not [string]::IsNullOrWhiteSpace($title)) {
      $procId = 0
      [void][ButchiWindowCapture]::GetWindowThreadProcessId($hWnd, [ref]$procId)
      $titles.Add(("$procId`t$title"))
    }
    return $true
  }
  [void][ButchiWindowCapture]::EnumWindows($callback, [IntPtr]::Zero)
  return $titles
}

function Find-WindowByTitleOrProcess {
  param([string]$Title, [int]$TargetPid)

  $hwnd = [ButchiWindowCapture]::FindWindow($null, $Title)
  if ($hwnd -ne [IntPtr]::Zero) { return $hwnd }

  # Fallback: enumerate visible windows that belong to the target process and
  # match the expected title (or any non-empty title owned by the process).
  if ($TargetPid -gt 0) {
    $found = [IntPtr]::Zero
    $callback = [ButchiWindowCapture+EnumWindowsProc]{
      param([IntPtr]$hWnd, [IntPtr]$lParam)
      if (-not [ButchiWindowCapture]::IsWindowVisible($hWnd)) { return $true }
      $owner = 0
      [void][ButchiWindowCapture]::GetWindowThreadProcessId($hWnd, [ref]$owner)
      if ($owner -ne $TargetPid) { return $true }
      $sb = New-Object System.Text.StringBuilder 512
      [void][ButchiWindowCapture]::GetWindowText($hWnd, $sb, $sb.Capacity)
      $t = $sb.ToString()
      if ($t -eq $Title -or ($Title -and $t.StartsWith("Butchi"))) {
        $script:found = $hWnd
        return $false
      }
      return $true
    }
    [void][ButchiWindowCapture]::EnumWindows($callback, [IntPtr]::Zero)
    if ($found -ne [IntPtr]::Zero) { return $found }
  }
  return [IntPtr]::Zero
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$hwnd = [IntPtr]::Zero
while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
  if ($ProcessId -gt 0) {
    try {
      $proc = Get-Process -Id $ProcessId -ErrorAction Stop
      if ($proc.HasExited) {
        throw "Butchi process $ProcessId exited before the capture window appeared (exit=$($proc.ExitCode))."
      }
    } catch [System.Management.Automation.ItemNotFoundException] {
      throw "Butchi process $ProcessId is no longer running."
    }
  }

  $hwnd = Find-WindowByTitleOrProcess -Title $WindowTitle -TargetPid $ProcessId
  if ($hwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
}

if ($hwnd -eq [IntPtr]::Zero) {
  $titles = Get-VisibleWindowTitles
  Write-Host "Visible top-level windows:"
  $titles | Select-Object -First 40 | ForEach-Object { Write-Host "  $_" }
  throw "Window '$WindowTitle' was not found within $TimeoutSeconds seconds."
}

$rect = New-Object ButchiWindowCapture+RECT
if (-not [ButchiWindowCapture]::GetWindowRect($hwnd, [ref]$rect)) {
  throw "Could not read bounds for '$WindowTitle'."
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 200 -or $height -lt 200) {
  throw "Implausible window bounds for '$WindowTitle': ${width}x${height}"
}

# Retry focus a few times — CI runners sometimes need a second push.
for ($i = 0; $i -lt 3; $i++) {
  [void][ButchiWindowCapture]::SetForegroundWindow($hwnd)
  Start-Sleep -Milliseconds 250
}
Start-Sleep -Milliseconds 500

$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

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

if (-not (Test-Path $OutputPath)) { throw "Capture was not created: $OutputPath" }
if ((Get-Item $OutputPath).Length -lt 10000) {
  throw "Capture is unexpectedly small: $OutputPath ($((Get-Item $OutputPath).Length) bytes)"
}

Write-Host "Captured '$WindowTitle' -> $OutputPath (${width}x${height})"
