using System;
using System.ComponentModel;

namespace LineageOS_ROM_Downloader;

/// <summary>
/// ファイルの種類や名称を定数として管理します。
/// </summary>
public static class FileTypes
{
    /// <summary>
    /// ROM
    /// </summary>
    public struct Rom
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "-signed.zip";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "rom";
    }
    
    /// <summary>
    /// Boot
    /// </summary>
    public struct Boot
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "boot";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "boot.img";
    }
    
    /// <summary>
    /// dtbo
    /// </summary>
    public struct Dtbo
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "dtbo";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "dtbo.img";
    }
    
    /// <summary>
    /// recovery
    /// </summary>
    public struct Recovery
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "recovery";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "recovery.img";
    }
    
    /// <summary>
    /// init_boot
    /// </summary>
    public struct InitBoot
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "init_boot";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "init_boot.img";
    }
    
    /// <summary>
    /// super_empty
    /// </summary>
    public struct SuperEmpty
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "super_empty";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "super_empty.img";
    }
    
    /// <summary>
    /// vbmeta
    /// </summary>
    public struct Vbmeta
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "vbmeta";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "vbmeta.img";
    }
    
    /// <summary>
    /// vendor_boot
    /// </summary>
    public struct VendorBoot
    {
        /// <summary>
        /// ファイル名
        /// </summary>
        public const string FileName = "vendor_boot";

        /// <summary>
        /// 略称
        /// </summary>
        public const string ShortName = "vendor_boot.img";
    }

    /// <summary>
    /// ヘルプ表示用のキーワード一覧
    /// </summary>
    public static IReadOnlyList<string> AllKeywords { get; } =
    [
        Rom.ShortName,
        Boot.ShortName,
        Dtbo.ShortName,
        Recovery.ShortName,
        InitBoot.ShortName,
        SuperEmpty.ShortName,
        Vbmeta.ShortName,
        VendorBoot.ShortName
    ];
}
