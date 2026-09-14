# CFDEngine 仿真结果查看器 —— 一键启动本地静态服务
#
# 浏览器在 file:// 下会阻止 fetch，所以必须走 HTTP。
# 优先用零依赖的 node serve.js；node 不可用时退回 python -m http.server。
#
# 用法：
#     .\serve.ps1            # 默认 8080
#     .\serve.ps1 9000       # 指定端口

param(
    [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

Write-Host ""
Write-Host "  CFDEngine 仿真结果查看器" -ForegroundColor Cyan
Write-Host "  站点根目录: $root" -ForegroundColor DarkGray
Write-Host ""

$node = Get-Command node -ErrorAction SilentlyContinue

if ($node) {
    Write-Host "  使用 node 启动: http://localhost:$Port/viz/index.html" -ForegroundColor Green
    Push-Location $here
    try {
        & node serve.js $Port
    }
    finally {
        Pop-Location
    }
    return
}

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }

if ($python) {
    Write-Host "  未找到 node，改用 python: http://localhost:$Port/viz/index.html" -ForegroundColor Yellow
    Push-Location $root
    try {
        & $python -m http.server $Port
    }
    finally {
        Pop-Location
    }
    return
}

Write-Host "  错误: 既没有 node 也没有 python，无法启动本地服务。" -ForegroundColor Red
Write-Host "  请安装 Node.js (>=18) 后重试，或手动用任意静态服务器托管 '$root'。" -ForegroundColor Red
