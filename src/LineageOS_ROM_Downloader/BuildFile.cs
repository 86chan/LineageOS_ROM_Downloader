using System.Text.Json.Serialization;
using static LineageOS_ROM_Downloader.FileTypes;

namespace LineageOS_ROM_Downloader;

/// <summary>
/// ビルドファイル情報
/// </summary>
public record BuildFile
{
    /// <summary>ファイル名</summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    /// <summary>ダウンロードURL</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>SHA256ハッシュ値</summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    /// <summary>
    /// -imgオプションなどで利用するファイル種別のキーワード
    /// </summary>
    /// <remarks>
    /// ファイル名からキーワードを判定します。
    /// JSONのデシリアライズ時には無視されます。
    /// </remarks>
    [JsonIgnore]
    public string TypeKeyword => Filename switch
    {
        var f when f.EndsWith(Rom.FileName) => Rom.ShortName,
                        Boot.FileName       => Boot.ShortName,
                        Dtbo.FileName       => Dtbo.ShortName,
                        Recovery.FileName   => Recovery.ShortName,
                        InitBoot.FileName   => InitBoot.ShortName,
                        SuperEmpty.FileName => SuperEmpty.ShortName,
                        Vbmeta.FileName     => Vbmeta.ShortName,
                        VendorBoot.FileName => VendorBoot.ShortName,
                                          _ => Filename // 一致しない場合はファイル名自身をキーワードとする
    };
}
