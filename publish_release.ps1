# 路径配置
$projectRoot = $PSScriptRoot
$sourceDir   = Join-Path $projectRoot "bin\Release"
$tempDir     = Join-Path $projectRoot "_publish"
$outZip      = Join-Path $projectRoot "opc_ae_relay.zip"

# 清理临时目录
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# 复制根文件
Copy-Item "$sourceDir\*.exe"              $tempDir -Force
Copy-Item "$sourceDir\*.exe.config"       $tempDir -Force
Copy-Item "$sourceDir\application.xml"    $tempDir -Force

# 复制 view
Copy-Item "$sourceDir\view" $tempDir -Recurse -Force

# 复制 lib：只排除 *.xml 和 *.pdb，其他全部保留
$libSource = Join-Path $sourceDir "lib"
$libDest   = Join-Path $tempDir "lib"

robocopy $libSource $libDest /E /XF *.xml *.pdb /NFL /NDL /NJH /NJS

# 打包
if (Test-Path $outZip) {
    Remove-Item $outZip -Force
}
Compress-Archive -Path "$tempDir\*" -DestinationPath $outZip

# 清理临时目录
Remove-Item $tempDir -Recurse -Force

Write-Host "Build success: $outZip"
Read-Host "Press Enter to exit"