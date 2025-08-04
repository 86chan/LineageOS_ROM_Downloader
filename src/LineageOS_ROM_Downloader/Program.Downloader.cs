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

                // Progress<T> は IProgress<T> の具体的な実装。
                // Reportメソッドが呼ばれるたびに、指定したラムダ式が実行される。
                var stopwatch = Stopwatch.StartNew();
                var progressReporter = new Progress<DownloadProgress>(progress =>
                {
                    var speed = progress.TotalBytesRead / (stopwatch.Elapsed.TotalSeconds + 1e-9) / 1024; // KB/s

                    // ここでUIの更新やコンソールへの表示を行う
                    Console.Write($"\r進捗: {progress.ProgressPercentage:F2}% " +
                                  $"({progress.TotalBytesRead / 1024.0 / 1024.0:F2} MB / {progress.TotalBytes / 1024.0 / 1024.0:F2} MB) " +
                                  $"速度: {speed:F2} KB/s");
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
}