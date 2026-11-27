#!/usr/bin/env dotnet
#:package ProcessX@2.0.0

// Unity Editor（Windows Build Support (IL2CPP) を含む）とライセンスを用意する、オンボーディングと版更新の単一の入口。
//   install [<project>]          ProjectVersion.txt（版の正本）に書かれた Editor を入れる。clone 直後に onboarding.ps1 から呼ばれる。
//   update <version> [<project>] 指定版を入れてプロジェクトを変換し、tools/util の build-windows でビルドまで確かめる。
// <project> は Unity プロジェクトを含むフォルダー（リポジトリのルートなど）。省略時は現在のフォルダー。
// 何度実行してもよいように、済んでいる手順は飛ばす。

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ProcessX;
using static ProcessX.Zx;

const string Module = "windows-il2cpp";

// NOTE: unity は UTF-8 で日本語を含む JSON を出すため、既定のコンソールのコードページ（CP932）で読むと化けて解析に失敗する。
Console.OutputEncoding = Encoding.UTF8;

try
{
    return args switch
    {
        ["install", .. var rest] when rest.Length <= 1 => await Install(rest.FirstOrDefault()),
        ["update", var version, .. var rest] when rest.Length <= 1 => await Update(
            version,
            rest.FirstOrDefault()
        ),
        _ => Usage(),
    };
}
catch (ProcessErrorException e)
{
    Console.Error.WriteLine($"[fail] exit={e.ExitCode}: {e.CommandLine}");
    return e.ExitCode == 0 ? 1 : e.ExitCode;
}
catch (Exception e) when (e is InvalidOperationException or OperationCanceledException)
{
    Console.Error.WriteLine($"[fail] {e.Message}");
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine(
        "usage: dotnet run .config/unity-install.cs -- install [<project>] | update <version> [<project>]"
    );
    return 2;
}

static async Task<int> Install(string? root)
{
    root = Path.GetFullPath(root ?? Environment.CurrentDirectory);
    var project = FindUnityProject(root);
    if (project is null)
    {
        Console.WriteLine($"Unity プロジェクトが見つからないため飛ばします: {root}");
        return 0;
    }

    var (version, changeset) = ReadProjectVersion(project);
    Console.WriteLine($"== Unity Editor {version}");
    await EnsureUnityCli();
    await EnsureSignedIn();
    await EnsureEditor(version, changeset);
    await EnsureLicense();
    return 0;
}

static async Task<int> Update(string version, string? root)
{
    root = Path.GetFullPath(root ?? Environment.CurrentDirectory);
    var project = FindUnityProject(root)
        ?? throw new InvalidOperationException($"Unity プロジェクトが見つかりません: {root}");

    // 変換で書き換わる前の作業を混ぜないよう、未コミットの変更があれば止める。
    var dirty = await run($"git status --porcelain --untracked-files=all -- {project}")
        .Cwd(root)
        .Quiet();
    if (dirty.StdOut.Trim().Length != 0)
    {
        throw new InvalidOperationException(
            $"版を切り替える前に {project} の変更をコミットしてください。"
        );
    }

    Console.WriteLine($"== Unity Editor {version} へ更新");
    await EnsureUnityCli();
    await EnsureSignedIn();
    // 切り替え先は ProjectVersion.txt にまだ書かれていないため、チェンジセットは CLI に解決させる。
    await EnsureEditor(version, changeset: null);
    await EnsureLicense();

    // Editor を開いて変換し、ProjectVersion.txt を新しい版へ書き換えさせる。
    var log = Path.Combine(root, "results", $"unity-update-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.log");
    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
    Console.WriteLine($"== プロジェクトの変換 log={log}");
    await run(
            $"unity run {project} --editor-version {version} --timeout 3600 --log-file {log} -- -accept-apiupdate"
        )
        .Cwd(root);

    Console.WriteLine("== ビルドの確認");
    await run(
            $"dotnet run --project {Path.Combine(root, "tools", "util", "util.csproj")} -- build-windows"
        )
        .Cwd(root)
        .Inherit();
    Console.WriteLine($"完了しました: {version}");
    return 0;
}

// プロジェクトごとのフォルダー構成に依存しないよう、ProjectSettings 直下の ProjectVersion.txt を探す。
static string? FindUnityProject(string root) =>
    Directory.Exists(root)
        ? Directory
            .EnumerateFiles(
                root,
                "ProjectVersion.txt",
                new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 }
            )
            .Select(Path.GetDirectoryName)
            .Where(dir => Path.GetFileName(dir) == "ProjectSettings")
            .Select(Path.GetDirectoryName)
            .FirstOrDefault()
        : null;

// 例: m_EditorVersionWithRevision: 6000.7.0b11 (0123456789ab)
static (string Version, string Changeset) ReadProjectVersion(string project)
{
    var path = Path.Combine(project, "ProjectSettings", "ProjectVersion.txt");
    var match = Regex.Match(
        File.ReadAllText(path),
        @"^m_EditorVersionWithRevision:\s*(\S+)\s*\((\w+)\)",
        RegexOptions.Multiline
    );
    return match.Success
        ? (match.Groups[1].Value, match.Groups[2].Value)
        : throw new InvalidOperationException($"Editor の版を読めませんでした: {path}");
}

// NOTE: Unity CLI は Hub が初回起動時に入れるため、未起動なら起動してもらい PATH に現れるまで待つ。
// 起動中のプロセスは導入前の PATH を持つので、毎回レジストリから読み直す。
static async Task EnsureUnityCli()
{
    var warned = false;
    while (true)
    {
        Environment.SetEnvironmentVariable(
            "PATH",
            $"{Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)};{Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)}"
        );
        if (which("unity") is not null)
        {
            return;
        }
        if (!warned)
        {
            Console.WriteLine(
                "Unity CLI が見つかりません。スタートメニューから Unity Hub を一度起動してください（入るまで待ちます）。"
            );
            warned = true;
        }
        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}

static async Task EnsureSignedIn()
{
    // 未サインインは失敗ではなく判定結果なので、終了コードで例外にしない。
    var status = await run($"unity auth status --format json").Quiet().NoThrow();
    if (JsonNode.Parse(status.StdOut)?["data"]?["loggedIn"]?.GetValue<bool>() == true)
    {
        Console.WriteLine("Unity にサインイン済みのため飛ばします。");
        return;
    }
    await run($"unity auth login").Inherit();
}

static async Task EnsureEditor(string version, string? changeset)
{
    var installed = await QueryItems(run($"unity editors --installed --format json"));
    if (!installed.Any(item => item?["version"]?.GetValue<string>() == version))
    {
        // 利用規約の確認は本人が答えるよう、--accept-eula は付けない。
        // 同じ版名でも中身を取り違えないよう、分かっていればチェンジセットも指定する。
        string[] changesetArgs = changeset is null ? [] : ["--changeset", changeset];
        await run($"unity install {version} {changesetArgs} --module {Module}").Inherit();
        return;
    }

    var modules = await QueryItems(run($"unity editors module {version} --format json"));
    if (
        modules.Any(item =>
            item?["id"]?.GetValue<string>() == Module
            && item["isInstalled"]?.GetValue<bool>() == true
        )
    )
    {
        Console.WriteLine($"導入済みのため飛ばします: {version}（{Module} を含む）");
        return;
    }
    await run($"unity editors module add {version} --module {Module}").Inherit();
}

static async Task EnsureLicense()
{
    // Asset Store などの Editor 以外のライセンスは数えない。
    var licenses = (await QueryItems(run($"unity license --format json")))
        .Select(item => item?["product"]?.GetValue<string>())
        .Where(product => product?.StartsWith("Unity ", StringComparison.Ordinal) == true)
        .ToArray();
    if (licenses.Length > 0)
    {
        Console.WriteLine($"ライセンスが有効なため飛ばします: {licenses[0]}");
        return;
    }

    // NOTE: activate --personal は --accept-eula が必須で CLI 自身は同意を尋ねないため、ここで規約を示して答えてもらう。
    Console.WriteLine(
        "Unity Personal（無料）のライセンスを有効にします。利用条件（収益の上限など）を含む規約:"
    );
    Console.WriteLine("  https://unity.com/legal/terms-of-service");
    Console.Write("規約に同意して有効にしますか？ (y/N) ");
    if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
    {
        throw new OperationCanceledException("ライセンスの有効化を中止しました。");
    }
    await run($"unity license activate --personal --accept-eula").Inherit();
}

static async Task<JsonArray> QueryItems(Command command)
{
    var result = await command.Quiet();
    return JsonNode.Parse(result.StdOut)?["data"]?["items"]?.AsArray() ?? [];
}
