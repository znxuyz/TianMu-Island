# 天幕島 · 一鍵出爐腳本
# 用法（在專案根目錄的 PowerShell 中）：
#   .\scripts\publish.ps1            # 產出 x64 版
#   .\scripts\publish.ps1 -Rid win-arm64
param(
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\$Rid"

Write-Host "🔥 開爐煉製天幕島 ($Rid) ..." -ForegroundColor Yellow

dotnet publish "$root\src\TianMu.Island\TianMu.Island.csproj" `
    -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true `
    -o $out

if ($LASTEXITCODE -ne 0) { throw "煉製失敗，請查看上方錯誤訊息。" }

Write-Host "✨ 法寶出爐：$out\TianMuIsland.exe" -ForegroundColor Green
