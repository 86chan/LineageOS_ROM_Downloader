using System.Text.Json.Serialization;

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

public static partial class Program
{
    /// <summary>
    /// ダウンロードの進捗状況を保持するデータ構造
    /// </summary>
    /// <param name="totalBytes">ダウンロードするファイルの総バイト数</param>
    /// <param name="totalBytesRead">これまでにダウンロードされたバイト数</param>
    public readonly struct DownloadProgress(long totalBytes, long totalBytesRead)
    {
        /// <summary>
        /// ダウンロードするファイルの総バイト数
        /// </summary>
        public long TotalBytes { get; } = totalBytes;

        /// <summary>
        /// これまでにダウンロードされたバイト数
        /// </summary>
        public long TotalBytesRead { get; } = totalBytesRead;

        /// <summary>
        /// ダウンロードの進捗率 (0-100)
        /// </summary>
        public double ProgressPercentage => TotalBytes > 0
                                          ? (double)TotalBytesRead / TotalBytes * 100
                                          : 0;
    }
}
