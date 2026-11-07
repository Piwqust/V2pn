<#
.SYNOPSIS
  Saves a PNG of V2pn's windows (or any process's windows), even when they are behind others.

.EXAMPLE
  tools/shot.ps1 -ProcessId 1234 -Out $env:TEMP/main.png
  tools/shot.ps1 -ProcessId 1234 -Out $env:TEMP/shots -All   # every visible window of the process
#>
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Out,
    # Saves every visible top-level window into the -Out folder instead of only the largest one.
    [switch]$All
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class V2pnShot {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> Windows(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
"@

function Save-Window([IntPtr]$h, [string]$path) {
    $r = New-Object V2pnShot+RECT
    [void][V2pnShot]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $hgt = $r.B - $r.T
    if ($w -le 1 -or $hgt -le 1) { return $false }
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # 2 = PW_RENDERFULLCONTENT: captures GPU-composed content such as Avalonia's.
    [void][V2pnShot]::PrintWindow($h, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "$path  ($w x $hgt)  '$([V2pnShot]::Title($h))'"
    return $true
}

$wins = [V2pnShot]::Windows([uint32]$ProcessId)
if ($wins.Count -eq 0) { throw "No visible windows for process $ProcessId" }

if ($All) {
    New-Item -ItemType Directory -Force $Out | Out-Null
    $i = 0
    foreach ($h in $wins) { [void](Save-Window $h (Join-Path $Out ("window-{0}.png" -f $i++))) }
}
else {
    # Popup layers can be as large as the window but untitled and empty; prefer titled windows.
    $best = $wins | Sort-Object @{ Expression = { [V2pnShot]::Title($_) -eq '' } }, @{ Expression = {
        $r = New-Object V2pnShot+RECT; [void][V2pnShot]::GetWindowRect($_, [ref]$r); - (($r.R - $r.L) * ($r.B - $r.T))
    } } | Select-Object -First 1
    New-Item -ItemType Directory -Force (Split-Path -Parent ([IO.Path]::GetFullPath($Out))) | Out-Null
    [void](Save-Window $best $Out)
}
