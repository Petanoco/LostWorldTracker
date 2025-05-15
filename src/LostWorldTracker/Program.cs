using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using ClientConfiguration = VRChat.API.Client.Configuration;
using Microsoft.Extensions.Configuration;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using CsvHelper;
using System.Windows.Documents;
using System.Security.Cryptography;
using System.Text;
using System.Net;

namespace LostWorldTracker;

internal class Program
{
    private static IConfiguration _appConfig = new ConfigurationBuilder().AddJsonFile(Constants.Path.AppConfigFile).Build();

    static void ProcessWithApi(ApiClient client, ClientConfiguration clientConfig)
    {
        var sleepMilliSeconds = 10000;
        int.TryParse(_appConfig[Constants.Config.ApiDelay], out sleepMilliSeconds);
        var Sleep = () => Task.Delay(sleepMilliSeconds).GetAwaiter().GetResult();
        Console.WriteLine($"APIリクエスト間隔: {sleepMilliSeconds}ms. (application.jsonから変更できます)\n");


        var favoriteApi = new FavoritesApi(client, client, clientConfig);
        var worldApi = new WorldsApi(client, client, clientConfig);
        var authApi = new AuthenticationApi(client, client, clientConfig);


        var currentUser = authApi.GetCurrentUser();


        // Favoriteの一覧を取得. こちらには非公開を含め全てのワールドIDが入っている
        var limit = favoriteApi.GetFavoriteLimits();
        var totalWorldFavoriteLimit = limit.MaxFavoritesPerGroup.World * limit.MaxFavoriteGroups.World;
        List<Favorite> favorites = new();
        for (int i = 0; i < totalWorldFavoriteLimit; i += 100)
        {
            Console.Write($"\rFavoriteデータを取得中... {i,4}/{totalWorldFavoriteLimit,4}");
            Sleep();
            var result = favoriteApi.GetFavorites(n: 100, offset: i, type: "world");
            favorites.AddRange(result);
        }
        Console.WriteLine($"\rFavoriteデータを取得中... {totalWorldFavoriteLimit,4}/{totalWorldFavoriteLimit,4}");


        // FavoriteWorld情報をグループ毎に取得. こちらは非公開ワールドの場合, 一部がマスクされる
        var favoriteGroup = favoriteApi.GetFavoriteGroups(n: 100);
        var favoriteGroupNames = favoriteGroup.Where(g => g.Type == FavoriteType.World).Select(g => g.Name).ToList();

        List<FavoritedWorld> favoriteWorlds = new();
        foreach (var groupName in favoriteGroupNames)
        {
            Console.Write($"\rFavoriteGroupデータを取得中... {favoriteGroupNames.IndexOf(groupName),1}/{favoriteGroupNames.Count(),1}");
            Sleep();
            favoriteWorlds.AddRange(worldApi.GetFavoritedWorlds(n: 100, offset: 0, userId: currentUser.Id, tag: groupName));
        }
        Console.WriteLine($"\rFavoriteGroupデータを取得中... {favoriteGroupNames.Count(),1}/{favoriteGroupNames.Count(),1}");


        // FavoriteAPIとWorldAPIの結果を結合
        Console.WriteLine($"\rFavoriteデータを結合");
        var favoriteWorldsDescription = from world in favoriteWorlds
                                        join favorite in favorites on world.FavoriteId equals favorite.Id
                                        join fvGroup in favoriteGroup on world.FavoriteGroup equals fvGroup.Name
                                        select new { Favorite = favorite, Description = world, Group = fvGroup };
        if (bool.Parse(_appConfig[Constants.Config.PrivateWorldOnly]!))
        {
            favoriteWorldsDescription = favoriteWorldsDescription.Where(d => d.Description.ReleaseStatus == ReleaseStatus.Private);
        }


        // プライベートワールドの情報を個別に取得
        var privateWorlds = favoriteWorldsDescription.Where(d => d.Description.ReleaseStatus == ReleaseStatus.Private).ToList();
        foreach (var world in privateWorlds)
        {
            Console.Write($"\rPrivateWorldを取得中... {privateWorlds.IndexOf(world),4}/{privateWorlds.Count(),4}");
            Sleep();
            try
            {
                var result = worldApi.GetWorld(world.Favorite.FavoriteId);
                world.Description.AuthorName = result.AuthorName;
                world.Description.Id = result.Id;
                world.Description.Name = result.Name;
            }
            catch (ApiException ex)
            {
                if (ex.ErrorCode == 404)
                    continue;
                throw;
            }
        }
        Console.WriteLine($"\rPrivateWorldを取得中... {privateWorlds.Count(),4}/{privateWorlds.Count(),4}");


        // 整形と出力
        var outdata = favoriteWorldsDescription.Select(d => new
        {
            FavGroup = d.Group.DisplayName,
            WorldId = d.Favorite.FavoriteId,
            Name = d.Description.Name,
            Author = d.Description.AuthorName,
            Description = d.Description.Description,
            Link = $"https://vrchat.com/home/world/{d.Favorite.FavoriteId}",
            Reference = $"https://www.vrcw.net/world/detail/{d.Favorite.FavoriteId}",
            Status = d.Description.ReleaseStatus == ReleaseStatus.Public ? "Public" :
                     d.Description.ReleaseStatus == ReleaseStatus.Private ? "Private" :
                     "Deleted",
            Created = d.Description.CreatedAt,
            Published = d.Description.PublicationDate,
            LastUpdate = d.Description.UpdatedAt
        });


        switch (_appConfig[Constants.Config.OutputFormat])
        {
            case "json":
                var jsonOutput = JsonConvert.SerializeObject(outdata, formatting: Formatting.Indented);
                Console.WriteLine(jsonOutput);
                System.IO.File.WriteAllText(Constants.Path.OutputJson, jsonOutput);
                Console.WriteLine($"結果が`{Constants.Path.OutputJson}`に出力されました");
                break;
            case "csv":
                using (var write = new StreamWriter(Constants.Path.OutputCsv))
                using (var csv = new CsvWriter(write, System.Globalization.CultureInfo.InvariantCulture))
                {
                    csv.WriteRecords(outdata);
                }
                Console.WriteLine($"結果が`{Constants.Path.OutputCsv}`に出力されました");
                break;
            default:
                throw new ArgumentException($"In appsettings.json, value {_appConfig[Constants.Config.OutputFormat]} is invalid.", Constants.Config.OutputFormat);
        }
        Console.WriteLine("Complete.");
    }

    static void Logout(ApiClient client, ClientConfiguration clientConfig)
    {
        var authApi = new AuthenticationApi(client, client, clientConfig);
        var result = authApi.Logout();
        Console.WriteLine(result._Success.Message);
    }

    [STAThread]
    static void Main(string[] args)
    {

        if (args.Length == 0 || args[0] == Constants.Mode.Normal.ToString("d"))
        {
            ApiClient client = new ApiClient();
            var clientConfig = new ClientConfiguration()
            {
                UserAgent = Constants.UserAgent,
            };

            // 認証情報の検証
            bool LoadAndVerifyCookies()
            {
                if (LoadCookies(clientConfig))
                {
                    try
                    {
                        AuthenticationApi authApi = new AuthenticationApi(client, client, clientConfig);
                        var currentUserRaw = authApi.GetCurrentUserWithHttpInfo();
                        return !currentUserRaw.RawContent.Contains("requiresTwoFactorAuth");
                    }
                    catch (ApiException ex)
                    {
                        switch (ex.ErrorCode)
                        {
                            case 401: // Unauthorized
                                Console.WriteLine($"401: {ex.ErrorContent}");
                                break;
                            default:
                                Console.WriteLine("Exception when calling API: {0}", ex.Message);
                                Console.WriteLine("Status Code: {0}", ex.ErrorCode);
                                Console.WriteLine(ex.ToString());
                                throw;
                        }
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("no cookies.");
                    return false;
                }
            }
            if (!LoadAndVerifyCookies())
            {
                // Cookieなし || ログイン失敗ならばWebView2でログインする
                LoginAndSaveCookiesByWebView2();
                if (!LoadAndVerifyCookies())
                {
                    // 再検証してエラーならば落とす
                    throw new Exception("ログイン認証に失敗しました");
                }
            }


            ProcessWithApi(client, clientConfig);
        }
        else if (args[0] == Constants.Mode.Logout.ToString("d"))
        {
            ApiClient client = new ApiClient();
            var clientConfig = new ClientConfiguration()
            {
                UserAgent = Constants.UserAgent,
            };
            if (LoadCookies(clientConfig))
            {
                Logout(client, clientConfig);
            }
        }

        Console.WriteLine("\nPress any key to continue . . .");
        Console.ReadKey(true);
    }

    public static bool LoadCookies(ClientConfiguration clientConfig, bool useDpapi = true)
    {
        var cookieFile = Path.Combine(AppContext.BaseDirectory, Constants.Path.CookieFile);
        if (!System.IO.File.Exists(cookieFile)) return false;

            byte[] raw = System.IO.File.ReadAllBytes(cookieFile);

            string json = useDpapi
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser))
                : Encoding.UTF8.GetString(raw);

            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (dict is null || dict.Count == 0) return false;

            string header = string.Join("; ", dict.Select(kvp => $"{kvp.Key}={kvp.Value}"));

            clientConfig.DefaultHeaders["Cookie"] = header;
            Console.WriteLine($"Cookies loaded");
            return true;
        }
    public static void SaveCookies(IDictionary<string, string> cookies, bool useDpapi = true)
    {
        var cookieFile = Path.Combine(AppContext.BaseDirectory, Constants.Path.CookieFile);
        string json = JsonConvert.SerializeObject(cookies);
        byte[] data = Encoding.UTF8.GetBytes(json);

        if (useDpapi)
            data = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

        System.IO.File.WriteAllBytes(cookieFile, data);
        Console.WriteLine($"Cookies saved");
    }

    static bool LoginAndSaveCookiesByWebView2()
    {
        Console.WriteLine("内蔵ブラウザでログインします。ログイン後にブラウザ画面を閉じてください。");
        var (isSuccess, cookies) = OpenWebView2WindowForLoginAsync().GetAwaiter().GetResult();
        if (isSuccess)
        {
            SaveCookies(cookies);
            var keyValuePairs = cookies.Select(c => $"{c.Key}={c.Value}");
            var headers = String.Join("; ", keyValuePairs);
        }
        return isSuccess;
    }

    /// <returns>Is logging-in succeeded</returns>
    static async Task<(bool IsSuccess, IDictionary<string, string> Cookies)> OpenWebView2WindowForLoginAsync()
    {
        string udf = Path.Combine(AppContext.BaseDirectory, Constants.Path.WebView2UserData); // Constants.Path.WebView2UserData = "WebView2Data";
        Directory.CreateDirectory(udf);

        var tcs = new TaskCompletionSource<(bool IsSuccess, IDictionary<string, string> Cookies)>();
        var uiThread = new Thread(() =>
        {
            var wpfApp = new Application();
            var webView = new WebView2()
            {
                CreationProperties = new CoreWebView2CreationProperties()
                {
                    UserDataFolder = udf
                }
            };
            var window = new Window
            {
                Title = "VRChat",
                Width = 800,
                Height = 800,
                Content = webView
            };

            webView.SourceChanged += (_, _) =>
            {
                // ログイン後ページに遷移したら閉じる
                if (webView.Source.AbsoluteUri == Constants.Path.VrcHomePage)
                {
                    window.Close();
                }
            };

            // UIスレッド終了中にCookieを取得できないため、一旦キャンセルしてからCookieを取得、その後閉じ直す
            bool isCookieLoading = true;
            window.Closing += (_, e) =>
            {
                if (isCookieLoading)
                {
                    e.Cancel = true;
                    SynchronizationContext.Current!.Post(async _ =>
                    {
                        Console.WriteLine(webView.CoreWebView2.Environment.BrowserVersionString);
                        await webView.EnsureCoreWebView2Async();
                        var cookies = (await webView.CoreWebView2.CookieManager.GetCookiesAsync(Constants.Path.VrcDomain).ConfigureAwait(true)).ToDictionary(c => c.Name, c => c.Value);
                        var hasAuth = cookies.ContainsKey(Constants.CookieKey.Auth);
                        tcs.TrySetResult((hasAuth, cookies));
                    }, null);
                    window.Dispatcher.BeginInvoke(window.Close);
                    isCookieLoading = false;
                }
            };

            webView.Source = new Uri(Constants.Path.VrcLoginPage);
            wpfApp.Run(window);
            
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();


        return await tcs.Task;
    }
}
