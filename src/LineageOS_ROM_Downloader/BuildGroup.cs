using System.Text.Json.Serialization;

namespace LineageOS_ROM_Downloader;

/// <summary>
/// 日付ごとのビルドグループ
/// </summary>
/// <remarks>
/// 1つのグループには、ROM本体やboot.imgなど、同日にビルドされた複数のファイルが含まれます。
/// </remarks>
public record BuildGroup
{
    /// <summary>ビルド日時 (Unix時間)</summary>
    [JsonPropertyName("datetime")]
    public long Datetime { get; init; }

    /// <summary>ビルドファイルのリスト</summary>
    [JsonPropertyName("files")]
    public required List<BuildFile> Files { get; init; }

    /// <summary>
    /// ダウンロード先の日付フォルダ名
    /// </summary>
    /// <remarks>
    /// yyyy-MM-dd形式の文字列を返します。JSONのデシリアライズ時には無視されます。
    /// </remarks>
    [JsonIgnore]
    public string DateDirectoryName => DateTimeOffset.FromUnixTimeSeconds(Datetime).ToString("yyyy-MM-dd");
}
