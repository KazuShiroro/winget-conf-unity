using ProcessX;
using static ProcessX.Zx;

namespace Util.Commands;

/// <summary>build-windows コマンドの実装。</summary>
/// <remarks>
/// Unity 側にビルドスクリプトを置かないため、-executeMethod ではなく Editor 標準の -activeBuildProfile と -build でビルドする。
/// NOTE: 6000.7 では -build に -buildTarget を組み合わせると拒否され、Build Profile のアセットが必須になる。
/// スクリプティングバックエンド（IL2CPP）は ProjectSettings、シーンは Build Profile（既定は共有のシーンリスト）に従う。
/// batchmode の起動とログは公式 Unity CLI の run に任せる。
/// </remarks>
internal static class BuildWindowsCommand
{
    private const string BuildProfile = "Assets/Settings/Build Profiles/Windows.asset";

    /// <summary>
    /// Windows Player をビルドする。
    /// </summary>
    /// <param name="output">-o, 出力する exe のパス。省略時は Unity.Client/Builds/StandaloneWindows64/Player.exe</param>
    /// <param name="timeout">Editor の終了を待つ秒数</param>
    public static async Task<int> Run(string? output = null, int timeout = 3600)
    {
        var repoRoot = RepoPaths.ResolveRepoRoot();
        var project = RepoPaths.UnityProject(repoRoot);
        var version = RepoPaths.ReadEditorVersion(project);
        output = Path.GetFullPath(
            output ?? Path.Combine(project, "Builds", "StandaloneWindows64", "Player.exe")
        );
        var log = Path.Combine(
            project,
            "Builds",
            "Logs",
            $"build-windows-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.log"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        // NOTE: 失敗の判定を終了コードだけに頼らず、出力の有無でも確かめるため、前回の exe を消してからビルドする。
        File.Delete(output);
        Console.WriteLine($"[build-windows] unity_version={version} output={output} log={log}");
        var result = await run(
                $"unity run {project} --editor-version {version} --timeout {timeout} --log-file {log} -- -activeBuildProfile {BuildProfile} -build {output}"
            )
            .Cwd(repoRoot)
            .NoThrow();

        if (result.ExitCode != 0 || !File.Exists(output))
        {
            Console.Error.WriteLine(
                $"[fail] build-windows exit={result.ExitCode} exists={File.Exists(output)} log={log}"
            );
            return result.ExitCode == 0 ? 1 : result.ExitCode;
        }
        Console.WriteLine($"[ok] {output}");
        return 0;
    }
}
