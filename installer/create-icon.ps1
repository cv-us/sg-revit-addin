Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::new("$PSScriptRoot\..\src\Shared\UI\Resources\icons\sprinkler-layout-32.png")
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.FileStream]::new("$PSScriptRoot\icon.ico", [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()
$icon.Dispose()
$bmp.Dispose()
Write-Host "icon.ico created from sprinkler-layout-32.png"

