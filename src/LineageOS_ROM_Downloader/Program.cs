using static LineageOS_ROM_Downloader.FileTypes;

namespace LineageOS_ROM_Downloader;

/// <summary>
/// LineageOS ダウンローダーのメインプログラム
/// </summary>
public partial class Program
{
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
        int maxThreads = 1;

        // コマンドライン引数をパースして、各変数に格納
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d": if (i + 1 < args.Length) device = args[++i]; break;
                case "-p": if (i + 1 < args.Length) rootDownloadDirectory = args[++i]; break;
                case "-img": if (i + 1 < args.Length) imgTypes.Add(args[++i]); break;
                case "-mt": if (i + 1 < args.Length) maxThreads = int.Parse(args[++i]); break;
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
            Console.WriteLine("LineageOS ダウンローダー");
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
            
            var deviceDownloadDir = Path.Combine(rootDownloadDirectory, device);
            Directory.CreateDirectory(deviceDownloadDir);


            // 対象の各ファイルについて、ダウンロードまたは移動処理を実行
            foreach (var fileToDownload in filesToProcess)
            {
                await ProcessFileAsync(
                    client, fileToDownload, maxThreads, latestGroup, previousGroup, deviceDownloadDir);
            }

            // 最新ビルド以外の古い日付フォルダを削除
            CleanupOldBuilds(latestGroup, deviceDownloadDir);
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
使い方: {execName} -d <デバイス名> -p <ダウンロード先> [-img <種類>] [-mt <スレッド数>] [...]
　または
使い方: {execName} --research -d <デバイス名>

オプション:
  -d        (必須) デバイスのコードネーム (例: renoir)
  -p        (必須) ダウンロード先のルートパス
  -img      (任意) ダウンロードするファイルの種類。複数指定可能。
            種類: {string.Join(", ", AllKeywords)}, または特定のファイル名
  -mt       (任意) ダウンロード時の最大並列スレッド数。既定値は1です。
  --research  指定されたデバイスで利用可能なファイルの種類を調査します。

使用例:
  {execName} -d renoir -p ./LineageBuilds -img rom -img recovery
  {execName} -d renoir -p ./LineageBuilds -img rom -mt 4
  {execName} --research -d renoir");
    }
}
