<#
.SYNOPSIS
    版本发布助手：更新版本号、提交、打 tag，推送后由 GitHub Actions 自动构建并发布 Release。

.DESCRIPTION
    实际的构建与发布由 .github/workflows/release.yml 完成，本脚本只负责把版本号写进
    仓库并打出 vX.Y.Z 标签。推送标签即触发发布流水线。

.EXAMPLE
    pwsh ./tools/release.ps1 -Version 0.3.2
    # 本地完成版本提交与打 tag，随后按提示手动推送

.EXAMPLE
    pwsh ./tools/release.ps1 -Version 0.3.2 -Push
    # 一步到位：提交、打 tag 并推送，直接触发 CI 发布
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Version,

    # 提交并打 tag 后直接推送到 origin，触发 Release 流水线
    [switch] $Push
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $Version = $Version.Trim().TrimStart('v')
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "版本号非法: '$Version'，期望 X.Y.Z 或 X.Y.Z-beta.1"
    }
    $tag = "v$Version"

    if (git status --porcelain) {
        throw '工作区不干净，请先提交或暂存改动'
    }
    if (git tag --list $tag) {
        throw "标签 $tag 已存在"
    }

    $csproj = 'DreamUnrealManager/DreamUnrealManager.csproj'
    $readme = 'README.md'

    $csprojText = Get-Content $csproj -Raw
    $updated = [regex]::Replace($csprojText, '<Version>[^<]*</Version>', "<Version>$Version</Version>", 1)
    if ($updated -eq $csprojText) {
        throw "未能在 $csproj 中找到 <Version> 节点"
    }
    Set-Content -Path $csproj -Value $updated -NoNewline

    # README 顶部的版本徽章
    $readmeText = Get-Content $readme -Raw
    $badge = [regex]::Replace($readmeText, 'Version-[0-9][^-]*-22C55E', "Version-$Version-22C55E", 1)
    if ($badge -ne $readmeText) {
        Set-Content -Path $readme -Value $badge -NoNewline
    }
    else {
        Write-Warning "README 版本徽章未匹配，已跳过"
    }

    git add -- $csproj $readme
    git commit -m "release: $tag"
    git tag -a $tag -m "Dream Unreal Manager $tag"

    if ($Push) {
        $branch = (git rev-parse --abbrev-ref HEAD).Trim()
        git push origin $branch
        git push origin $tag
        Write-Host "已推送 $tag，前往 Actions 查看发布进度" -ForegroundColor Green
    }
    else {
        Write-Host "已在本地打好 $tag。推送以触发发布：" -ForegroundColor Yellow
        Write-Host "  git push origin HEAD && git push origin $tag"
    }
}
finally {
    Pop-Location
}
