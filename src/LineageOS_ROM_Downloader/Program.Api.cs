using System.Text.Json;

namespace LineageOS_ROM_Downloader;

public static partial class Program
{
    /// <summary>
    /// 指定されたデバイスのAPIエンドポイントURLを生成します。
    /// </summary>
    /// <param name="device">デバイスのコードネーム</param>
    /// <returns>APIのエンドポイントURL</returns>
    private static string GetApiUrl(string device) => $"https://download.lineageos.org/api/v2/devices/{device}/builds";

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
}