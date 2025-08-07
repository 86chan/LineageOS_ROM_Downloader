namespace LineageOS_ROM_Downloader;

public static partial class Program
{
    /// <summary>
    /// ダウンロード対象ファイルのフィルタリング
    /// </summary>
    /// <param name="allFiles">フィルタリング対象の全ファイルリスト</param>
    /// <param name="requestedTypes">-imgオプションで指定されたファイル種別のリスト</param>
    /// <returns>フィルタリング後のファイルリスト</returns>
    /// <remarks>
    /// -img オプションが指定されていない場合は、すべてのファイルを返します。
    /// </remarks>
    private static List<BuildFile> FilterFiles(List<BuildFile> allFiles, List<string> requestedTypes)
    {
        // -imgオプションが指定されていない場合は、すべてのファイルを対象とする
        if (requestedTypes.Count == 0) return allFiles;

        Console.WriteLine($"\n-> -img オプションに基づいてダウンロード対象をフィルタリング中...");

        // 要求されたファイル種別をHashSetに格納し、高速な検索を可能にする(大文字小文字は区別しない)
        var requestedTypesSet = new HashSet<string>(requestedTypes, StringComparer.OrdinalIgnoreCase);

        // ファイルのキーワードが要求された種別セットに含まれているものだけを抽出
        var filteredList = allFiles
            .Where(file => requestedTypesSet.Contains(file.TypeKeyword))
            .ToList();

        Console.WriteLine($" -> {filteredList.Count} 個のファイルが一致しました。");
        return filteredList;
    }

    /// <summary>
    /// 単一ファイルの処理
    /// </summary>
    /// <param name="client">HTTP通信に使用するHttpClientインスタンス</param>
    /// <param name="fileToDownload">処理対象のファイル情報</param>
    /// <param name="latestGroup">最新のビルドグループ</param>
    /// <param name="previousGroup">一つ前のビルドグループ（存在する場合）</param>
    /// <param name="rootDownloadDir">ダウンロード先のルートディレクトリ</param>
    private static async Task ProcessFileAsync(HttpClient client,
                                               BuildFile fileToDownload,
                                               int maxThreads,
                                               BuildGroup latestGroup,
                                               BuildGroup? previousGroup,
                                               string rootDownloadDir)
    {
        Console.WriteLine($"\n--- ファイル処理中: {fileToDownload.Filename} ---");

        // 最新ビルド用の日付フォルダを作成
        string latestDirPath = Path.Combine(rootDownloadDir, latestGroup.DateDirectoryName);
        Directory.CreateDirectory(latestDirPath);

        // ダウンロード先のパスと、検証成功を示すマーカーファイルのパスを定義
        string destinationPath = Path.Combine(latestDirPath, fileToDownload.Filename);
        string markerFilePath = destinationPath + ".sha256";

        // すでに検証済みのファイルはスキップ
        if (File.Exists(markerFilePath))
        {
            Console.WriteLine(" -> 最新バージョンはダウンロード・検証済みです。");
            return;
        }

        // 一つ前のビルドに同じファイル(ハッシュが一致)が存在するかチェック
        if (previousGroup != null)
        {
            var prevFile = previousGroup.Files.FirstOrDefault(f => f.Filename == fileToDownload.Filename);
            if (prevFile != null && prevFile.Sha256.Equals(fileToDownload.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                string prevFilePath = Path.Combine(rootDownloadDir, previousGroup.DateDirectoryName, fileToDownload.Filename);

                // 存在すれば、ダウンロードせずにファイルを移動して処理を完了
                if (File.Exists(prevFilePath))
                {
                    Console.WriteLine(" -> 更新がないため、旧ビルドからファイルを移動します。");
                    File.Move(prevFilePath, destinationPath);
                    await File.WriteAllTextAsync(markerFilePath, fileToDownload.Sha256);
                    Console.WriteLine($" -> マーカーファイルを作成しました。");
                    return;
                }
            }
        }

        // 新規ダウンロードと検証処理を実行
        bool success = await TryDownloadAndVerifyAsync(client, fileToDownload, destinationPath, maxThreads);
        if (success)
        {
            // 成功した場合、マーカーファイルを作成
            await File.WriteAllTextAsync(markerFilePath, fileToDownload.Sha256);
            Console.WriteLine($" -> マーカーファイルを作成しました。");
        }
        else
        {
            // 失敗した場合、エラーメッセージを表示
            Console.ForegroundColor = ConsoleColor.Red;
            var msg = $" -> エラー: {MaxRetries} 回試行しましたが、'{fileToDownload.Filename}' のダウンロードと検証に失敗しました。";
            Console.WriteLine(msg);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 旧バージョンの日付フォルダを削除
    /// </summary>
    /// <param name="latestGroup">最新のビルドグループ情報</param>
    /// <param name="rootDownloadDir">ダウンロード先のルートディレクトリ</param>
    private static void CleanupOldBuilds(BuildGroup latestGroup, string rootDownloadDir)
    {
        Console.WriteLine("\n--- 旧バージョンのクリーンアップ ---");
        var deletedCount = 0;

        // ルートディレクトリ内のすべての日付フォルダをチェック
        foreach (var dirPath in Directory.GetDirectories(rootDownloadDir))
        {
            var dirName = Path.GetFileName(dirPath);
            // 最新ビルドのフォルダでなければ削除対象とする
            if (dirName != latestGroup.DateDirectoryName)
            {
                try
                {
                    Directory.Delete(dirPath, true);
                    Console.WriteLine($" -> フォルダを削除しました: {dirName}");
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($" -> エラー: フォルダ '{dirName}' の削除に失敗しました。({ex.Message})");
                    Console.ResetColor();
                }
            }
        }

        // 削除結果に応じてメッセージを表示
        string message = deletedCount > 0
                       ? $" -> {deletedCount} 個の古いビルドフォルダを削除しました。"
                       : " -> 削除する古いビルドフォルダはありません。";
        Console.WriteLine(message);
    }
}