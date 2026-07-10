# Generate unique placeholder icons for each ribbon command
Add-Type -AssemblyName System.Drawing

$outputDir = "$PSScriptRoot\..\src\Shared\UI\Resources\icons"

function New-Icon($filename, $text, $bgHex, $fgHex, $size) {
    $bgColor = [System.Drawing.ColorTranslator]::FromHtml($bgHex)
    $fgColor = [System.Drawing.ColorTranslator]::FromHtml($fgHex)

    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $brush = New-Object System.Drawing.SolidBrush($bgColor)
    $g.FillEllipse($brush, 1, 1, $size - 2, $size - 2)

    $fontSize = if ($text.Length -le 1) { $size * 0.5 } else { $size * 0.36 }
    $font = New-Object System.Drawing.Font("Arial", $fontSize, [System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush($fgColor)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $g.DrawString($text, $font, $textBrush, $rect, $sf)

    $g.Dispose()
    $path = Join-Path $outputDir "$filename.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Make-Pair($prefix, $text, $bg, $fg) {
    New-Icon "$prefix-32" $text $bg $fg 32
    New-Icon "$prefix-16" $text $bg $fg 16
    Write-Host "  $prefix : $text" -ForegroundColor Gray
}

Write-Host "Generating icons..." -ForegroundColor Cyan

# Panel colors
$blue    = "#2196F3"; $green  = "#4CAF50"; $orange = "#FF9800"
$red     = "#F44336"; $cyan   = "#00BCD4"; $brown  = "#795548"
$purple  = "#9C27B0"; $dgray  = "#37474F"; $indigo = "#3F51B5"
$gblu    = "#607D8B"; $teal   = "#009688"; $amber  = "#FFA000"
$W = "#FFFFFF"; $B = "#000000"

# Sprinkler Layout (blue)
Make-Pair "sprinkler-layout" "SP" $blue $W

# Pipe Routing (green)
Make-Pair "pipe-routing" "PR" $green $W
Make-Pair "shorten-flex" "SF" $green $W

# Hangers - placement (orange)
Make-Pair "hangers"          "H"  $orange $W
Make-Pair "hang-cad"         "HC" $orange $W
Make-Pair "hang-struct"      "HS" $orange $W
Make-Pair "hang-downstream"  "HD" $orange $W
Make-Pair "hang-spacing"     "HT" $orange $W
Make-Pair "hang-parallel"    "HP" $orange $W
Make-Pair "hang-userloc"     "HU" $orange $W
Make-Pair "hang-tee"         "CT" $orange $W
Make-Pair "hang-trapeze"     "TP" $orange $W
Make-Pair "hang-trapeze-ul"  "TU" $orange $W
Make-Pair "hang-unistrut"    "UN" $orange $W
Make-Pair "hang-uni21a"      "UA" $orange $W
Make-Pair "format-ticks"     "FT" $orange $W
Make-Pair "section-ids"      "SI" $orange $W
Make-Pair "swap-hydracad"    "SW" $orange $W
Make-Pair "sync-pipes"       "sP" $orange $W
Make-Pair "sync-refplane"    "sR" $orange $W
Make-Pair "sync-raybounce"   "sB" $orange $W
Make-Pair "sync-surface"     "sS" $orange $W
Make-Pair "sync-trapeze"     "sT" $orange $W
Make-Pair "flip-trapeze"     "FL" $orange $W

# Seismic (red)
Make-Pair "seismic" "SB" $red $W

# Hydraulics (cyan)
Make-Pair "hydraulics" "HY" $cyan $W
Make-Pair "fluid-delivery" "WD" $cyan $W

# Fabrication (brown)
Make-Pair "fabrication" "CL" $brown $W

# Coordination (purple)
Make-Pair "coordination" "CC" $purple $W
Make-Pair "color-pipes"  "CP" $purple $W

# Export (teal)
Make-Pair "trimble-points"    "TP" $teal $W
Make-Pair "trimble-markers"   "TM" $teal $W
Make-Pair "import-pipes"      "iP" $teal $W
Make-Pair "import-sprinklers" "iS" $teal $W

# Annotation (dark gray)
Make-Pair "annotation"        "A"  $dgray $W
Make-Pair "pipe-elevations"   "PE" $dgray $W
Make-Pair "flex-drop"         "FD" $dgray $W
Make-Pair "flex-auto"         "FA" $dgray $W
Make-Pair "scale-bars"        "GB" $dgray $W
Make-Pair "sleeve-elevations" "SE" $dgray $W
Make-Pair "sleeves-beams"     "SB" $dgray $W
Make-Pair "sleeves-decks"     "SD" $dgray $W
Make-Pair "sleeves-walls"     "SW" $dgray $W
Make-Pair "room-text"         "RT" $dgray $W
Make-Pair "beam-penetration"  "BP" $dgray $W
Make-Pair "ssb-symbols"       "SS" $dgray $W
Make-Pair "delete-dupe-text"  "DT" $dgray $W
Make-Pair "clear-annotations" "CA" $dgray $W
Make-Pair "seismic-braces"    "SZ" $red $W

# Views (indigo)
Make-Pair "views"            "V"  $indigo $W
Make-Pair "duplicate-views"  "DV" $indigo $W
Make-Pair "plan-views"       "PV" $indigo $W
Make-Pair "dependent-views"  "dV" $indigo $W
Make-Pair "rotate-scopebox"  "RS" $indigo $W
Make-Pair "remove-scopebox"  "rS" $indigo $W

# Setup (gray-blue)
Make-Pair "setup"         "G"  $gblu $W
Make-Pair "load-families" "LF" $gblu $W
Make-Pair "copy-levels"   "CL" $gblu $W
Make-Pair "global-params" "GP" $gblu $W
Make-Pair "clear-params"  "cP" $gblu $W

# Model Check (amber/black)
Make-Pair "modelcheck"          "Q"  $amber $B
Make-Pair "sprinkler-clearance" "SC" $amber $B
Make-Pair "deflector-distance"  "DD" $amber $B
Make-Pair "pipes-too-short"     "PS" $amber $B

Write-Host "`nDone!" -ForegroundColor Green

