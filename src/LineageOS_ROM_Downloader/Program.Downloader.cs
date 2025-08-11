using System.Diagnostics;
using System.Security.Cryptography;

namespace LineageOS_ROM_Downloader;

public static partial class Program
{
    /// <summary>
    /// ダウンロード失敗時の最大再試行回数
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// 再試行までの待機時間（秒）
    /// </summary>
    private const int RetryDelaySeconds = 5;

    /// <summary>
    /// 再試行付きでのファイルダウンロードと検証
    /// </summary>
    /// <param name="client">HTTP通信に使用するHttpClientインスタンス</param>
    /// <param name="fileToDownload">ダウンロード対象のファイル情報</param>
    /// <param name="destinationPath">ファイルの保存先パス</param>
    /// <param name="maxThreads">ダウンロード時の最大並列スレッド数</param>
    /// <returns>処理が成功した場合は<c>true</c>、失敗した場合は<c>false</c></returns>
    private static async Task<bool> TryDownloadAndVerifyAsync(HttpClient client,
                                                              BuildFile fileToDownload,
                                                              string destinationPath,
                                                              int maxThreads)
    {
        // 最大再試行回数までループ
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // ファイルのダウンロードを実行
                Console.WriteLine($"[試行 {attempt}/{MaxRetries}] ダウンロードと検証を開始します...");

                // 並列ダウンロードが有効な場合は、並列ダウンロードを実行
                if (maxThreads > 1)
                {
                    await DownloadFileInParallelAsync(client, fileToDownload.Url, destinationPath, maxThreads);
                }
                else
                {
                    // シングルスレッドでダウンロードを実行
                    var stopwatch = Stopwatch.StartNew();
                    var progressReporter = new Progress<DownloadProgress>(progress =>
                    {
                        var speed = progress.TotalBytesRead / (stopwatch.Elapsed.TotalSeconds + 1e-9); // B/s
                        DisplayDownloadProgress(progress.TotalBytes, progress.TotalBytesRead, speed);
                    });

                    try
                    {
                        await DownloadFileWithProgressAsync(client, fileToDownload.Url, destinationPath, progressReporter);
                        stopwatch.Stop();
                        Console.WriteLine("\nダウンロードが完了しました。");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nメイン処理でエラーをキャッチしました: {ex.Message}");
                    }
                }

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
    /// 指定されたURLからファイルを並列ダウンロードします。
    /// </summary>
    /// <param name="client">HttpClientインスタンス。</param>
    /// <param name="requestUri">ダウンロードするファイルのURL。</param>
    /// <param name="destinationPath">ファイルの保存先パス。</param>
    /// <param name="maxThreads">ダウンロードに使用する最大スレッド数。</param>
    public static async Task DownloadFileInParallelAsync(
        HttpClient client,
        string requestUri,
        string destinationPath,
        int maxThreads)
    {
        // ファイルの総サイズを取得
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, requestUri));
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ??
                         throw new InvalidOperationException("ファイルのサイズが取得できません。");

        Console.WriteLine($"ファイルサイズ: {totalBytes / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"最大 {maxThreads} スレッドで並列ダウンロードを開始します。");

        // ファイルを事前に確保（スパースファイルの作成）
        await CreateSparseFileAsync(destinationPath, totalBytes);

        // 各スレッドがダウンロードする範囲を計算
        var chunkSize = totalBytes / maxThreads;
        var ranges = new List<(long Start, long End)>();
        for (int i = 0; i < maxThreads; i++)
        {
            var start = i * chunkSize;
            var end = (i == maxThreads - 1) ? totalBytes - 1 : start + chunkSize - 1;
            ranges.Add((start, end));
        }

        long totalBytesRead = 0;
        var stopwatch = Stopwatch.StartNew();

        // 各範囲を並列にダウンロード
        var downloadTasks = ranges.Select(range => Task.Run(async () =>
        {
            // ダウンロードする範囲を指定してリクエストを作成
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Range = new(range.Start, range.End);

            using var partialResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            partialResponse.EnsureSuccessStatusCode();

            await using var contentStream = await partialResponse.Content.ReadAsStreamAsync();

            // ファイルの正しい位置に書き込むために、ファイルストリームを開き、書き込み位置を移動
            await using var fileStream = new FileStream(destinationPath, FileMode.Open, FileAccess.Write, FileShare.Write, 8192, true);
            fileStream.Seek(range.Start, SeekOrigin.Begin);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                Interlocked.Add(ref totalBytesRead, bytesRead);

                // グローバルな進捗を更新
                var speed = totalBytesRead / (stopwatch.Elapsed.TotalSeconds + 1e-9); // B/s
                DisplayDownloadProgress(totalBytes, totalBytesRead, speed);
            }
        })).ToList();

        await Task.WhenAll(downloadTasks);
        stopwatch.Stop();
        Console.WriteLine("\n並列ダウンロードが完了しました。");
    }

    /// <summary>
    /// IProgress<T> を使って、指定されたURLからファイルを非同期にダウンロードします。
    /// </summary>
    /// <param name="client">HttpClientインスタンス。</param>
    /// <param name="requestUri">ダウンロードするファイルのURL。</param>
    /// <param name="destinationPath">ファイルの保存先パス。</param>
    /// <param name="progress">進捗を報告するためのIProgress<DownloadProgress>インスタンス。</param>
    public static async Task DownloadFileWithProgressAsync(
        HttpClient client,
        string requestUri,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            // ヘッダーのみを先に読み込み、大きなファイルを効率的に扱う
            using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            if (!totalBytes.HasValue)
            {
                // 総容量が不明な場合は、進捗報告なしでダウンロード
                await DownloadFileWithoutProgressAsync(response, destinationPath);
                return;
            }

            using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath,
                                                        FileMode.Create,
                                                        FileAccess.Write,
                                                        FileShare.None,
                                                        8192, true);

            long totalBytesRead = 0;
            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalBytesRead += bytesRead;
                // 進捗報告オブジェクトを通じて、現在の状態を通知する
                progress?.Report(new DownloadProgress(totalBytes.Value, totalBytesRead));
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"\nダウンロード中にエラーが発生しました: {e.Message}");
            // 必要に応じて例外を再スローするなど、エラーハンドリングを強化できます
            throw;
        }
    }

    /// <summary>
    /// コンテンツ容量が不明な場合に、進捗表示なしでダウンロードするヘルパーメソッド。
    /// </summary>
    private static async Task DownloadFileWithoutProgressAsync(HttpResponseMessage response, string destinationPath)
    {
        using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destinationPath,
                                                    FileMode.Create,
                                                    FileAccess.Write,
                                                    FileShare.None,
                                                    8192, true);
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

    /// <summary>
    /// 指定されたサイズのスパースファイルを非同期に作成します。
    /// </summary>
    /// <param name="path">作成するファイルのパス。</param>
    /// <param name="length">ファイルのサイズ（バイト単位）。</param>
    /// <remarks>
    /// このメソッドは、ファイルを作成し、そのサイズを指定された長さに設定します。
    /// FileStreamを開き、SetLengthで長さを設定した後、すぐに閉じることで、
    /// 実際にディスクスペースを消費しないスパースファイルを作成します。
    /// これにより、後からファイル内の任意の位置に書き込むことが可能になります。
    /// </remarks>
    private static Task CreateSparseFileAsync(string path, long length)
    {
        return Task.Run(() =>
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.SetLength(length);
        });
    }

    /// <summary>
    /// ダウンロードの進捗状況をコンソールに表示
    /// </summary>
    /// <param name="totalBytes">ダウンロードする合計バイト数</param>
    /// <param name="totalBytesRead">既にダウンロードされたバイト数</param>
    /// <param name="bytesPerSecond">秒間あたりのダウンロード速度（バイト/秒）</param>
    /// <remarks>
    /// 進捗率、ダウンロード済みサイズ/合計サイズ、現在のダウンロード速度をコンソールに表示します。
    /// 例: 進捗: 50.00% (5.00 MB / 10.00 MB) 速度: 1.25 MB/s
    /// </remarks>
    private static void DisplayDownloadProgress(long totalBytes, long totalBytesRead, double bytesPerSecond)
    {
        // コンソールに進捗情報を一行で表示し、キャリッジリターン(\r)で常に行頭から上書きします
        Console.Write($"\r進捗: {(double)totalBytesRead / totalBytes * 100:F2}% " +
                      $"({totalBytesRead / 1024.0 / 1024.0:F2} MB / {totalBytes / 1024.0 / 1024.0:F2} MB) " +
                      $"速度: {FormatSpeed(bytesPerSecond)}");
    }

    /// <summary>
    /// バイト/秒の速度を適切な単位（KB/s, MB/s, GB/s）にフォーマット
    /// </summary>
    /// <param name="bytesPerSecond">秒間あたりのダウンロード速度（バイト/秒）</param>
    /// <returns>
    /// フォーマットされた速度を表す文字列
    /// </returns>
    /// <remarks>
    /// 速度に応じて単位を自動的に調整し、小数点以下2桁まで表示します。
    /// - 1MB未満: KB/s
    /// - 1GB未満: MB/s
    /// - 1GB以上: GB/s
    /// </remarks>
    private static string FormatSpeed(double bytesPerSecond)
    {
        // switch式を使用して、速度の値に応じて適切な単位に変換して返します
        return bytesPerSecond switch
        {
            // 1MB (1024 * 1024 bytes) 未満の場合は KB/s に変換
            < 1024 * 1024 => $"{bytesPerSecond / 1024:F2} KB/s",
            // 1GB (1024 * 1024 * 1024 bytes) 未満の場合は MB/s に変換
            < 1024 * 1024 * 1024 => $"{bytesPerSecond / (1024 * 1024):F2} MB/s",
            // それ以上の場合は GB/s に変換
            _ => $"{bytesPerSecond / (1024 * 1024 * 1024):F2} GB/s"
        };
    }
}

/// <summary>
/// ダウンロード進捗状況を保持するレコード
/// </summary>
/// <param name="TotalBytes">ダウンロード対象の総バイト数</param>
/// <param name="TotalBytesRead">現在までに読み込まれたバイト数</param>
public record DownloadProgress(long TotalBytes, long TotalBytesRead)
{
    /// <summary>
    /// 進捗率を0%から100%の範囲で計算します。
    /// </summary>
    public double ProgressPercentage => TotalBytes > 0 ? (double)TotalBytesRead / TotalBytes * 100 : 0;
}