# `just --list` で一覧を出す。

# NOTE: 通常のレシピは sh へ 1 行の文字列として渡すため、空白を含む引数の区切りが崩れる。
# pwsh のスクリプトとして実行し、引数を $args の配列のまま渡す。終了コードも exit $LASTEXITCODE でそのまま返す。
set positional-arguments
set script-interpreter := ["pwsh", "-NoLogo", "-NoProfile", "-File"]

lizard := "lizard@1.26.1"

# 初回セットアップ（.NET ツールと Git hooks）
[script]
setup:
    $ErrorActionPreference = 'Stop'
    $PSNativeCommandUseErrorActionPreference = $true
    dotnet tool restore
    dotnet husky install

# ProjectVersion.txt の版の Unity Editor とライセンスを用意する
[script]
unity-install *args:
    dotnet run .config/unity-install.cs -- install @args
    exit $LASTEXITCODE

# Unity Editor を指定版へ更新し、変換してビルドまで確かめる（例: just unity-update 6000.7.0b11）
[script]
unity-update version:
    dotnet run .config/unity-install.cs -- update $args[0]
    exit $LASTEXITCODE

# Windows Player ビルド
[script]
build-windows *args:
    dotnet run --project tools/util/util.csproj -- build-windows @args
    exit $LASTEXITCODE

# ステージ済みファイルの .meta の漏れ検出（pre-commit から呼ぶ）
[script]
check-unity-meta *args:
    dotnet run --project tools/util/util.csproj -- check-unity-meta @args
    exit $LASTEXITCODE

# C# の整形
[script]
format:
    dotnet csharpier format .
    exit $LASTEXITCODE

# C# の循環的複雑度の計測（閾値を超えた関数だけを出し、あれば失敗にする）
[script]
complexity *args:
    uvx {{lizard}} --languages csharp --CCN 10 --warnings_only --exclude "*/obj/*" --exclude "*/bin/*" .config tools @args
    exit $LASTEXITCODE
