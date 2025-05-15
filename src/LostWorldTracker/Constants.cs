using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LostWorldTracker;
internal static class Constants
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36 Edg/136.0.0.0";
    internal static class Config
    {
        public const string ApiDelay = "api_inter_request_delay";
        public const string OutputFormat = "output_format";
        public const string PrivateWorldOnly = "private_world_only";
    }
    internal static class CookieKey
    {
        public const string Auth = "auth";
        public const string Tfa = "twoFactorAuth";
    }
    internal static class Path
    {
        public const string AppConfigFile = "application.json";
        public const string CookieFile = "cookies";
        public const string WebView2UserData = "WebView2Data";
        public const string OutputJson = "output.json";
        public const string OutputCsv = "output.csv";
        public const string VrcDomain = "https://vrchat.com/";
        public const string VrcHomePage = "https://vrchat.com/home";
        public const string VrcLoginPage = "https://vrchat.com/home/login";
    }

    internal enum Mode
    {
        Normal = 0,
        Logout = 1,
        //ClearCookies = 2,
    }
    internal enum OutputFormat{ Json, Csv }
}
