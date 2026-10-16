# Build tnote.exe, launch it at 1000x600 and save a window screenshot (args: output png path, -File to open)
param([string]$Out = "$env:TEMP\tnote.png", [switch]$NoBuild, [string]$File)
$dir = $PSScriptRoot
if (-not $NoBuild) {
  New-Item -ItemType Directory -Force "$env:TEMP\npbuild" | Out-Null
  & C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /codepage:65001 /nowarn:108 /out:"$env:TEMP\npbuild\tnote.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /win32icon:"$dir\tnote.ico" "$dir\tnote.cs"
  if ($LASTEXITCODE -ne 0) { exit 1 }; Copy-Item "$env:TEMP\npbuild\tnote.exe" "$dir\tnote.exe" -Force
}
Add-Type -AssemblyName System.Drawing
Add-Type 'using System;using System.Runtime.InteropServices;public class W3{[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);[DllImport("user32.dll")]public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int hh,bool r);public struct RECT{public int L,T,R,B;}}'
$p = if ($File) { Start-Process "$env:TEMP\npbuild\tnote.exe" -ArgumentList "`"$File`"" -PassThru } else { Start-Process "$env:TEMP\npbuild\tnote.exe" -PassThru }
Start-Sleep 3; $p.Refresh()
[W3]::MoveWindow($p.MainWindowHandle, 100, 100, 1000, 600, $true) | Out-Null
[W3]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep 1
$r = New-Object W3+RECT; [W3]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$b = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
[Drawing.Graphics]::FromImage($b).CopyFromScreen($r.L, $r.T, 0, 0, $b.Size)
$b.Save($Out)
Stop-Process $p
"saved $Out ($($b.Width)x$($b.Height))"

