#Requires -Version 7.4
# onboarding.winget から別のウィンドウで開かれ、gh の認証、リポジトリの HTTPS clone、Unity Editor の導入を行う。
# 何度実行してもよいように、済んでいる手順は飛ばす。
$ErrorActionPreference = 'Stop'
# gh・git・dotnet が 0 以外の終了コードを返したら、その場で例外にして catch へ進める。
$PSNativeCommandUseErrorActionPreference = $true

try {
    Write-Host '== GitHub の認証' -ForegroundColor Cyan
    # 未認証は失敗ではなく判定結果なので、この呼び出しだけ例外にしない。
    & {
        $PSNativeCommandUseErrorActionPreference = $false
        gh auth status --hostname github.com *> $null
    }
    if ($LASTEXITCODE -eq 0) {
        Write-Host '認証済みのため飛ばします。'
    } else {
        gh auth login --hostname github.com --web --git-protocol https
    }
    # login 時の回答に左右されないよう、git が gh のトークンを使う設定を毎回入れる。
    gh auth setup-git --hostname github.com

    Write-Host '== clone' -ForegroundColor Cyan
    $repositoryFile = Join-Path $PSScriptRoot 'onboarding-repository.txt'
    $repository = if (Test-Path $repositoryFile) {
        @(Get-Content $repositoryFile | ForEach-Object Trim | Where-Object { $_ })[0]
    } else {
        Read-Host 'clone するリポジトリ（owner/name）'
    }
    if ("$repository".Trim() -notmatch '^[\w.-]+/[\w.-]+$') { throw "owner/name の形式ではありません: $repository" }
    $repository = $repository.Trim()
    $name = $repository.Split('/')[1]
    Write-Host "リポジトリ: $repository"

    # git clone は自分でフォルダーを作るため、選んでもらうのは親フォルダーにする。
    Write-Host 'clone 先を置くフォルダーを、開いたダイアログで選んでください。'
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = [System.Windows.Forms.FolderBrowserDialog]@{
        Description            = "clone 先を置くフォルダーを選んでください（この中に $name フォルダーを作ります）"
        UseDescriptionForTitle = $true
        InitialDirectory       = $env:USERPROFILE
    }
    # NOTE: 所有者を指定しないとダイアログがコンソールの後ろに隠れることがあるため、最前面のフォームを所有者にする。
    $owner = [System.Windows.Forms.Form]@{ TopMost = $true }
    try {
        $selected = $dialog.ShowDialog($owner)
        $parent = $dialog.SelectedPath
    } finally {
        $owner.Dispose()
        $dialog.Dispose()
    }
    if ($selected -ne [System.Windows.Forms.DialogResult]::OK) { throw 'clone 先が選ばれなかったため中止しました。' }
    $destination = Join-Path $parent $name
    if (Test-Path (Join-Path $destination '.git')) {
        Write-Host "clone 済みのため飛ばします: $destination"
    } elseif ((Test-Path $destination) -and @(Get-ChildItem -Force $destination).Count -gt 0) {
        throw "clone 先が空ではありません: $destination"
    } else {
        git clone "https://github.com/$repository.git" $destination
    }

    # NOTE: clone 先の .config ではなく、このスクリプトと一緒に配布した unity-install.cs を使う。
    # clone 先の global.json が求める SDK と食い違っても、onboarding 一式の版の組み合わせで動かすため。
    dotnet run --file (Join-Path $PSScriptRoot 'unity-install.cs') -- install $destination

    Write-Host ''
    Write-Host "完了しました: $destination" -ForegroundColor Green
    $exitCode = 0
} catch {
    Write-Host ''
    Write-Host "失敗しました: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
Read-Host 'Enter で閉じます'
exit $exitCode
