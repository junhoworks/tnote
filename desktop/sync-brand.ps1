# tNote does not draw its own icon: it copies the generated set from the shared T Brand repo
# (github.com/junhoworks/t-brand, tools\make-icons.ps1 -> dist\tnote\). Run after the brand icons change,
# then rebuild the exe:
#   powershell -ExecutionPolicy Bypass -File desktop\sync-brand.ps1 [-Brand C:\dev\t-brand]
param([string]$Brand = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 't-brand'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $Brand 'dist\tnote'
if (-not (Test-Path $src)) { throw "T Brand dist not found: $src  (clone github.com/junhoworks/t-brand next to this repo or pass -Brand)" }

# exe / window / title bar logo (the build embeds it with /win32icon, which takes DIB entries only, as T Brand writes them)
Copy-Item (Join-Path $src 'tnote.ico') (Join-Path $PSScriptRoot 'tnote.ico') -Force

# web app icon and favicon: the page embeds the 128px image as a data URI
$html = Join-Path $root 'web\index.html'
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $src 'tnote-128.png')))
$text = [IO.File]::ReadAllText($html)
$new = [regex]::Replace($text, '(<link rel="icon" id="favicon" href=")data:image/png;base64,[^"]*(")', "`${1}data:image/png;base64,$b64`${2}")
if ($new -eq $text -and -not $text.Contains($b64)) { throw "favicon link not found in $html" }
[IO.File]::WriteAllText($html, $new, (New-Object Text.UTF8Encoding $false))
"OK <- $src"
