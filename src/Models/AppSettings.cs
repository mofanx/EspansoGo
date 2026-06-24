using Microsoft.Maui.Storage;
namespace EspansoGo.Models
{
    internal static class AppSettings
    {
        public static List<string> SupportedList = new() { "echo", "date", "clipboard", "random", "choice" };
        public static string OldDictPath = Path.Combine(FileSystem.Current.CacheDirectory, "keywords.json");
        public static string DictPath = Path.Combine(FileSystem.Current.AppDataDirectory, "keywords.json");
        public static string GlobalVarsPath = Path.Combine(FileSystem.Current.AppDataDirectory, "global.json");
    }
}
