# winget-conf-unity

[WinGet Configuration](https://learn.microsoft.com/windows/package-manager/configuration/)（`.winget`）を使って、Windows の新しい PC で Unity の開発を始めるまでのオンボーディングをまとめたサンプルリポジトリです。

## オンボーディング

1. [`.config`](.config) フォルダーの `onboarding.winget`、`onboarding.ps1`、`onboarding-repository.txt`、`unity-install.cs` を同じフォルダーへダウンロードする。
2. `onboarding.winget` をダブルクリックする（またはターミナルで次を実行する）。

   ```sh
   winget configure -f onboarding.winget
   ```

構成は次の順に進みます。

1. Git、GitHub CLI、PowerShell 7、.NET SDK 11、mise、Unity Hub、Visual Studio Build Tools 2026 を winget で入れる。
2. 別のウィンドウで `gh` の認証をして、`onboarding-repository.txt` に書いたリポジトリを HTTPS で clone する。
3. `unity-install.cs` が、clone した `ProjectSettings/ProjectVersion.txt` の版の Unity Editor（Windows Build Support (IL2CPP) を含む）を入れ、Unity Personal のライセンスを有効にする。
4. Visual Studio Build Tools に C++ のワークロード（IL2CPP のビルドに要る）を足す。

利用規約（Unity の EULA と Unity Personal の規約）への同意は、各手順の途中でユーザーが答えます。

## clone 後のセットアップ

```sh
mise install
just setup
```

`just --list` でコマンドの一覧を出せます。

| コマンド | 内容 |
| --- | --- |
| `just unity-install` | `ProjectVersion.txt` の版の Unity Editor とライセンスを用意する |
| `just unity-update <version>` | Unity Editor を指定版へ更新し、プロジェクトを変換して Windows ビルドまで確かめる |
| `just build-windows` | Windows Player をビルドする（`Unity.Client/Builds/StandaloneWindows64`） |
| `just check-unity-meta` | ステージ済みファイルの `.meta` の漏れを検出する（pre-commit で実行） |
| `just format` | CSharpier で C# を整形する |
| `just complexity` | lizard で C# の循環的複雑度を計測する |

## 構成

```
.config/
  onboarding.winget        # WinGet Configuration（DSC v3）
  onboarding.ps1           # gh の認証と clone
  onboarding-repository.txt # clone するリポジトリ（owner/name）
  unity-install.cs         # Unity Editor の導入・更新（C# のファイルベースアプリ、ProcessX v2）
  dotnet-tools.json        # CSharpier、Husky.Net、EasyBuild.CommitLinter
.husky/                    # Git hooks（Husky.Net）
tools/util/                # build-windows と check-unity-meta（.NET 11 の CLI）
Unity.Client/              # Unity プロジェクト（版の正本は ProjectSettings/ProjectVersion.txt）
global.json                # .NET SDK の版
mise.toml                  # just と uv の版
justfile                   # コマンドの入口
```

## 補足

- `.config/dotnet-tools.json` のツールは `rollForward: true` にしています。オンボーディングで入るのは .NET 11 だけで、.NET 10 以前を対象にしたツールはそのままでは起動できないためです。
- Unity Editor の版の正本は `Unity.Client/ProjectSettings/ProjectVersion.txt` です。`unity-install.cs` は版を持たず、常にこのファイルから読みます。
