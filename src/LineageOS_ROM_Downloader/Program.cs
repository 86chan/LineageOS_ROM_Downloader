using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LineageOS_ROM_Downloader.FileTypes;


namespace LineageOS_ROM_Downloader;

/// <summary>
/// JSONソースジェネレータのコンテキスト
/// </summary>
/// <remarks>
/// アプリケーションのトリミング（不要コード削除）に対応するため、
/// 必要な型情報をコンパイル時に静的に解決します。
/// </remarks>
[JsonSerializable(typeof(List<BuildGroup>))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

/// <summary>
/// LineageOS ダウンローダーのメインプログラム
/// </summary>
public class Program
{
    // --- 定数 ---
    /// <summary>ダウンロード失敗時の最大再試行回数</summary>
    private const int MaxRetries = 3;
    /// <summary>再試行までの待機時間（秒）</summary>
    private const int RetryDelaySeconds = 5;

    /// <summary>
    /// 指定されたデバイスのAPIエンドポイントURLを生成します。
    /// </summary>
    /// <param name="device">デバイスのコードネーム</param>
    /// <returns>APIのエンドポイントURL</returns>
    private static string GetApiUrl(string device) => $"https://download.lineageos.org/api/v2/devices/{device}/builds";

    // --- メイン処理 ---
    /// <summary>
    /// アプリケーションのエントリーポイント
    /// </summary>
    /// <param name="args">コマンドライン引数。-d (デバイス名), -p (ダウンロード先), -img (ファイル種別)</param>
    public static async Task Main(string[] args)
    {
        string? device = null;
        string? rootDownloadDirectory = null;
        var imgTypes = new List<string>();
        bool researchMode = false;

        // コマンドライン引数をパースして、各変数に格納
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d": if (i + 1 < args.Length) device = args[++i]; break;
                case "-p": if (i + 1 < args.Length) rootDownloadDirectory = args[++i]; break;
                case "-img": if (i + 1 < args.Length) imgTypes.Add(args[++i]); break;
                case "--research": researchMode = true; break;
            }
        }

        // 必須引数であるデバイス名が指定されているか検証
        if (string.IsNullOrEmpty(device))
        {
            ShowUsage(researchMode);
            return;
        }

        using HttpClient client = new();
        var apiUrl = GetApiUrl(device);

        // リサーチモードが指定されている場合は、調査処理のみを実行して終了
        if (researchMode)
        {
            await ResearchAsync(client, device, apiUrl);
            return;
        }

        // 通常のダウンロードモードで必須の引数が指定されているか検証
        if (string.IsNullOrEmpty(rootDownloadDirectory))
        {
            ShowUsage();
            return;
        }

        try
        {
            // メインのダウンロード処理を開始
            Console.WriteLine("LineageOS ダウンローダー (日付フォルダ管理版)");
            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine($"対象デバイス: {device}");
            Console.WriteLine($"ルートディレクトリ: {Path.GetFullPath(rootDownloadDirectory)}");

            // APIからビルド情報を取得し、日付の降順でソート
            var sortedGroups = await FetchBuildGroupsAsync(client, apiUrl, device);
            if (sortedGroups is null || sortedGroups.Count == 0) return;

            // 最新と一つ前のビルド情報を取得
            var latestGroup = sortedGroups[0];
            var previousGroup = sortedGroups.Count > 1 ? sortedGroups[1] : null;

            // コマンドライン引数に基づいて、ダウンロード対象のファイルを絞り込み
            var filesToProcess = FilterFiles(latestGroup.Files, imgTypes);

            // 対象の各ファイルについて、ダウンロードまたは移動処理を実行
            foreach (var fileToDownload in filesToProcess)
            {
                await ProcessFileAsync(
                    client, fileToDownload, latestGroup, previousGroup, rootDownloadDirectory);
            }

            // 最新ビルド以外の古い日付フォルダを削除
            CleanupOldBuilds(latestGroup, rootDownloadDirectory);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // APIから404エラーが返された場合、デバイス名が間違っている可能性が高いことを示す
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nエラー: デバイス '{device}' が見つかりません。API URLを確認してください: {apiUrl}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            // 予期しないエラーが発生した場合、その内容を表示
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n未処理のエラーが発生しました: {ex.Message}");
            Console.ResetColor();
        }
    }

    // --- 個別の処理を記述したメソッド群 ---

    /// <summary>
    /// 利用可能なファイル種別の調査
    /// </summary>
    /// <param name="client">HTTP通信に使用するHttpClientインスタンス</param>
    /// <param name="device">調査対象のデバイス名</param>
    /// <param name="apiUrl">LineageOSのAPIエンドポイントURL</param>
    private static async Task ResearchAsync(HttpClient client, string device, string apiUrl)
    {
        Console.WriteLine($"デバイス '{device}' の利用可能なファイルを調査します...");

        try
        {
            // 最新のビルド情報を取得し、そこに含まれるファイルの一覧とキーワードを表示
            var sortedGroups = await FetchBuildGroupsAsync(client, apiUrl, device);
            if (sortedGroups is null || sortedGroups.Count == 0) return;

            var latestGroup = sortedGroups[0];
            Console.WriteLine($"\n最新ビルド ({latestGroup.DateDirectoryName}) で提供されるファイル一覧:");
            Console.WriteLine("------------------------------------------------------------------");
            Console.WriteLine("[-img オプションで指定可能なキーワード] : [実際のファイル名]");

            foreach (var file in latestGroup.Files)
            {
                Console.WriteLine($"   - {file.TypeKeyword,-15} : {file.Filename}");
            }
            Console.WriteLine("------------------------------------------------------------------");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nエラー: デバイス '{device}' が見つかりません。API URLを確認してください: {apiUrl}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n調査中にエラーが発生しました: {ex.Message}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// APIからビルドグループリストを取得し日付でソート
    /// </summary>
    /// <param name="client">HTTP通信に使用するHttpClientインスタンス</param>
    /// <param name="apiUrl">LineageOSのAPIエンドポイントURL</param>
    /// <param name="device">ダウンロード対象のデバイス名</param>
    /// <returns>日付の降順でソートされた<see cref="BuildGroup"/>のリスト。見つからない場合は<c>null</c></returns>
    private static async Task<List<BuildGroup>?> FetchBuildGroupsAsync(HttpClient client, string apiUrl, string device)
    {
        Console.WriteLine($"\n[1] '{device}' のビルドリストを取得中...");
        // APIにリクエストを送り、レスポンスをJSON文字列として取得
        var jsonString = await client.GetStringAsync(apiUrl);
        // ソースジェネレータを使用して、トリムセーフにJSONをデシリアライズ
        var buildGroups = JsonSerializer.Deserialize(jsonString, SourceGenerationContext.Default.ListBuildGroup);

        if (buildGroups is null || buildGroups.Count == 0)
        {
            Console.WriteLine("ビルドグループが見つかりません。");
            return null;
        }

        // ビルドの日時(Unix時間)で降順にソートして返す
        return buildGroups.OrderByDescending(g => g.Datetime).ToList();
    }

    /// <summary>
    /// ダウンロード対象ファイルのフィルタリング
    /// </summary>
    /// <param name="allFiles">フィルタリング対象の全ファイルリスト</param>
    /// <param name="requestedTypes">-imgオプションで指定されたファイル種別のリスト</param>
    /// <returns>フィルタリング後のファイルリスト</returns>
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
        bool success = await TryDownloadAndVerifyAsync(client, fileToDownload, destinationPath);
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
    /// 再試行付きでのファイルダウンロードと検証
    /// </summary>
    /// <param name="client">HTTP通信に使用するHttpClientインスタンス</param>
    /// <param name="fileToDownload">ダウンロード対象のファイル情報</param>
    /// <param name="destinationPath">ファイルの保存先パス</param>
    /// <returns>処理が成功した場合は<c>true</c>、失敗した場合は<c>false</c></returns>
    private static async Task<bool> TryDownloadAndVerifyAsync(HttpClient client,
                                                              BuildFile fileToDownload,
                                                              string destinationPath)
    {
        // 最大再試行回数までループ
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // ファイルのダウンロードを実行
                Console.WriteLine($"[試行 {attempt}/{MaxRetries}] ダウンロードと検証を開始します...");
                await DownloadFileAsync(client, fileToDownload.Url, destinationPath);
                Console.WriteLine(" -> ダウンロード完了。");

                // ダウンロードしたファイルのハッシュ値を計算
                Console.WriteLine(" -> ファイルの整合性を検証中...");
                var actualSha256 = await ComputeSha256Async(destinationPath);

                // APIから取得したハッシュ値と一致するか検証
                if (fileToDownload.Sha256.Equals(actualSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" -> 成功: ハッシュが一致しました。ファイルは正常です。");
                    Console.ResetColor();
                    return true; // 成功した場合はtrueを返してループを抜ける
                }

                // ハッシュが一致しない場合は、ファイルを削除して再試行
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" -> 警告: ハッシュが一致しません！ファイルは破損している可能性があります。");
                Console.ResetColor();
                File.Delete(destinationPath);
            }
            catch (Exception ex)
            {
                // ダウンロード中にエラーが発生した場合、警告を表示
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($" -> 警告: 試行 {attempt} 回目にエラーが発生しました: {ex.Message}");
                Console.ResetColor();
            }

            // 次の試行まで一定時間待機
            if (attempt < MaxRetries)
            {
                Console.WriteLine($" -> {RetryDelaySeconds} 秒待機して再試行します...");
                await Task.Delay(RetryDelaySeconds * 1000);
            }
        }
        return false; // すべての試行が失敗した場合はfalseを返す
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

    /// <summary>
    /// 使用方法を表示
    /// </summary>
    /// <param name="researchMode">リサーチモード用の表示を行うかどうか</param>
    private static void ShowUsage(bool researchMode = false)
    {
        // 実行時のアセンブリ名を取得し、OSに依存しない表示にする
        var execName = Path.GetFileName(Environment.ProcessPath ?? AppDomain.CurrentDomain.FriendlyName);
        if (researchMode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("エラー: --research オプションには -d <デバイス名> の指定が必須です。");
            Console.ResetColor();
            Console.WriteLine(@$"
使い方: {execName} --research -d <デバイス名>

使用例: {execName} --research -d renoir");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("エラー: 引数が不足しているか、無効です。");
        Console.ResetColor();
        Console.WriteLine(@$"
使い方: {execName} -d <デバイス名> -p <ダウンロード先> [-img <種類>] [...]
　または
使い方: {execName} --research -d <デバイス名>

オプション:
  -d        (必須) デバイスのコードネーム (例: renoir)
  -p        (必須) ダウンロード先のルートパス
  -img      (任意) ダウンロードするファイルの種類。複数指定可能。
            種類: {string.Join(", ", AllKeywords)}, または特定のファイル名
  --research  指定されたデバイスで利用可能なファイルの種類を調査します。

使用例:
  {execName} -d renoir -p ./LineageBuilds -img rom -img recovery
  {execName} --research -d renoir");
    }

    // --- ユーティリティメソッド ---
    /// <summary>
    /// URLからのファイルダウンロード
    /// </summary>
    /// <param name="client">HTTP通信に使用するHttpClientインスタンス</param>
    /// <param name="requestUri">ダウンロード元のURL</param>
    /// <param name="destinationPath">ファイルの保存先パス</param>
    private static async Task DownloadFileAsync(HttpClient client, string requestUri, string destinationPath)
    {
        // ヘッダーのみを先に読み込み、大きなファイルを効率的に扱う
        using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        // レスポンスのコンテンツをストリームとして取得
        using var contentStream = await response.Content.ReadAsStreamAsync();
        // ファイル書き込み用のストリームを開く
        await using var fileStream = new FileStream(destinationPath,
                                                    FileMode.Create,
                                                    FileAccess.Write,
                                                    FileShare.None,
                                                    8192,
                                                    true);
        // ダウンロードストリームをファイルストリームにコピー
        await contentStream.CopyToAsync(fileStream);
    }

    /// <summary>
    /// ファイルのSHA256ハッシュ値の計算
    /// </summary>
    /// <param name="filePath">ハッシュ値を計算するファイルのパス</param>
    /// <returns>計算されたSHA256ハッシュ値（小文字の16進数文字列）</returns>
    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var fileStream = File.OpenRead(filePath);
        // ファイルストリームから非同期でハッシュを計算
        var hash = await sha256.ComputeHashAsync(fileStream);
        // 計算されたバイト配列を小文字の16進数文字列に変換して返す
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
