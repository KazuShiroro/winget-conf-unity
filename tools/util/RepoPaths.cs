namespace Util;

/// <summary>リポジトリ内のパス解決。</summary>
internal static class RepoPaths
{
    /// <summary>
    /// カレントディレクトリから親へたどり、.git を含む最初のディレクトリをリポジトリルートとして返す。
    /// </summary>
    /// <remarks>
    /// dll の配置場所から逆算すると、ビルド構成で階層が変わり壊れるため、git と同じくカレントディレクトリを起点にする。
    /// git worktree では .git がファイルになるため両方を目印にする。
    /// </remarks>
    /// <exception cref="InvalidOperationException">リポジトリの外で実行された場合。</exception>
    public static string ResolveRepoRoot()
    {
        for (
            var directory = new DirectoryInfo(Environment.CurrentDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"リポジトリルートが見つかりません。リポジトリ内で実行してください: {Environment.CurrentDirectory}"
        );
    }

    /// <summary>Unity プロジェクトのルート（リポジトリ直下の Unity.Client）。</summary>
    public static string UnityProject(string repoRoot) => Path.Combine(repoRoot, "Unity.Client");

    /// <summary>ProjectVersion.txt（Editor の版の正本）から版を読む。</summary>
    /// <exception cref="InvalidOperationException">m_EditorVersion 行がない場合。</exception>
    public static string ReadEditorVersion(string unityProject)
    {
        var path = Path.Combine(unityProject, "ProjectSettings", "ProjectVersion.txt");
        const string Prefix = "m_EditorVersion:";
        return File.ReadLines(path)
            .FirstOrDefault(line => line.StartsWith(Prefix, StringComparison.Ordinal))
            ?[Prefix.Length..].Trim()
            ?? throw new InvalidOperationException($"Editor の版を読めませんでした: {path}");
    }
}
