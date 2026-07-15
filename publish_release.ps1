# ========== Path Config ==========
$projectRoot = $PSScriptRoot
$releasePath = Join-Path $projectRoot "bin\Release"
$outZipPath  = Join-Path $projectRoot "opc_ae_relay_release.zip"

# Go to release folder
Set-Location $releasePath

Write-Host "Work dir: $releasePath"
Write-Host "Output zip: $outZipPath`n"

# 1. Delete all pdb
Get-ChildItem -Recurse -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "Deleted all .pdb files"

# 2. Delete all xml under lib
$libPath = Join-Path $releasePath "lib"
if (Test-Path $libPath) {
    Get-ChildItem $libPath -Recurse -Filter *.xml -ErrorAction SilentlyContinue | Remove-Item -Force
}
Write-Host "Deleted all .xml files in lib"

# 3. Delete old zip
if (Test-Path $outZipPath) {
    Remove-Item $outZipPath -Force
}

# 4. Create zip
$files = Get-ChildItem $releasePath -Exclude *.ps1,*.zip
Compress-Archive -Path $files -DestinationPath $outZipPath

Write-Host "`nBuild success: $outZipPath"
Read-Host "Press Enter to exit"