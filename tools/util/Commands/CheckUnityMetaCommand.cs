using System.Text.RegularExpressions;
using ProcessX;
using static ProcessX.Zx;

namespace Util.Commands;

/// <summary>check-unity-meta コマンドの実装。</summary>
/// <remarks>
/// pre-commit で、ステージした Assets 配下のファイルに対応する .meta（ファイル自身と、Assets までの親フォルダ）の
/// ステージ漏れを検出する。.meta が漏れると、別の環境で GUID が作り直されて参照が切れるため。
/// 変更のない .meta や存在しない .meta は対象にしない（既にコミット済み、または Unity がまだ生成していないため）。
/// </remarks>
internal static class CheckUnityMetaCommand
{
    private static readonly Regex AssetsSegment = new("(^|/)Assets(/|$)");

    /// <summary>
    /// ステージ済みファイルの .meta のステージ漏れを検出する。
    /// </summary>
    /// <returns>漏れがなければ 0、あれば 1。</returns>
    public static async Task<int> Run()
    {
        var repoRoot = RepoPaths.ResolveRepoRoot();
        var staged = (
            await run($"git diff --cached --name-only --diff-filter=ACMR").Cwd(repoRoot).Quiet()
        )
            .StdOut.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToHashSet(StringComparer.Ordinal);

        var candidates = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in staged.Where(IsAssetFile))
        {
            candidates.Add(file + ".meta");
            for (
                var dir = ParentOf(file);
                dir.Length > 0 && AssetsSegment.IsMatch(dir);
                dir = ParentOf(dir)
            )
            {
                candidates.Add(dir + ".meta");
            }
        }

        var existing = candidates
            .Where(meta => !staged.Contains(meta) && File.Exists(Path.Combine(repoRoot, meta)))
            .ToArray();
        if (existing.Length == 0)
        {
            return 0;
        }

        // NOTE: 対象の .meta をまとめて 1 回の git status へ渡す。-z で区切ると、空白や日本語を含むパスも引用符なしで受け取れる。
        var missing = (await run($"git status --porcelain -z -- {existing}").Cwd(repoRoot).Quiet())
            .StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Length > 3 ? entry[3..] : entry)
            .Intersect(existing, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length == 0)
        {
            return 0;
        }

        Console.WriteLine("ERROR: .meta がステージされていません:");
        foreach (var meta in missing)
        {
            Console.WriteLine(meta);
        }
        Console.WriteLine();
        Console.WriteLine("以下のコマンドで .meta を追加してから再度コミットしてください:");
        Console.WriteLine("  git add " + string.Join(' ', missing.Select(meta => $"\"{meta}\"")));
        return 1;
    }

    private static bool IsAssetFile(string file) =>
        AssetsSegment.IsMatch(file) && !file.EndsWith(".meta", StringComparison.Ordinal);

    private static string ParentOf(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? "" : path[..index];
    }
}
