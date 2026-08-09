# 重建三個遊戲的「開始遊戲」捷徑（流程 MD §7.1）
#
# 什麼時候要跑：
#   - 換電腦、搬過資料夾之後（捷徑存的是絕對路徑，一定會壞）
#   - 重新打包之後
#
# 前置：三個專案都要先打包過
#   Unity.exe -batchmode -quit -projectPath <專案> -executeMethod GameBuild.BuildPlayable
#
# 用法：在本檔所在資料夾按右鍵 →「使用 PowerShell 執行」，或
#   powershell -ExecutionPolicy Bypass -File 建立捷徑.ps1

$root    = $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
$sh      = New-Object -ComObject WScript.Shell

$games = @(
  @{ folder = 'SumoParty';  exe = 'SumoParty.exe';  label = '相撲' },
  @{ folder = 'DriftGame';  exe = 'DriftGame.exe';  label = '甩尾' },
  @{ folder = '蚯蚓的一生'; exe = '蚯蚓的一生.exe'; label = '蚯蚓' }
)

function Set-Link($linkPath, $target, $workdir, $desc) {
  $l = $sh.CreateShortcut($linkPath)
  $l.TargetPath       = $target
  $l.WorkingDirectory = $workdir
  $l.IconLocation     = "$target,0"
  $l.Description      = $desc
  $l.Save()
}

$missing = @()
foreach ($g in $games) {
  $exe = Join-Path $root "$($g.folder)\Build\$($g.exe)"
  if (-not (Test-Path $exe)) {
    $missing += "$($g.label)（$($g.folder) 還沒打包）"
    continue
  }
  $wd = Split-Path $exe

  Set-Link (Join-Path $root "$($g.folder)\!開始遊戲.lnk")   $exe $wd "開始遊戲：$($g.label)"  # 各遊戲資料夾內
  Set-Link (Join-Path $root "!開始遊戲-$($g.label).lnk")     $exe $wd "開始遊戲：$($g.label)"  # 專案根目錄
  Set-Link (Join-Path $desktop "$($g.label)遊戲.lnk")        $exe $wd "開始遊戲：$($g.label)"  # 桌面

  Write-Host "OK  $($g.label) -> $exe" -ForegroundColor Green
}

if ($missing.Count -gt 0) {
  Write-Host ""
  Write-Host "以下還沒有執行檔，捷徑沒建立：" -ForegroundColor Yellow
  $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
  Write-Host "先打包再重跑本腳本。"
}
