using System.IO;
using System.Text.Json;
using EspansoGo.Models;

namespace EspansoGo.Services
{
    public class MigrationService
    {
        private readonly YamlWorkspace _yamlWorkspace;

        public MigrationService(YamlWorkspace yamlWorkspace)
        {
            _yamlWorkspace = yamlWorkspace;
        }

        public bool NeedsMigration => AppSettings.DataFormatVersion < 2;

        public bool MigrateIfNeeded()
        {
            if (!NeedsMigration)
                return true;

            return MigrateV1ToV2();
        }

        private bool MigrateV1ToV2()
        {
            if (!File.Exists(AppSettings.DictPath))
            {
                AppSettings.SetDataFormatVersion(2);
                return true;
            }

            try
            {
                var json = File.ReadAllText(AppSettings.DictPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, Match>>(json);

                if (dict == null || dict.Count == 0)
                {
                    AppSettings.SetDataFormatVersion(2);
                    return true;
                }

                File.Copy(AppSettings.DictPath, AppSettings.DictBackupPath, overwrite: true);

                Directory.CreateDirectory(AppSettings.KeywordsDir);

                var grouped = new Dictionary<string, List<Match>>();
                foreach (var kv in dict)
                {
                    var fileName = kv.Value.SourceFile ?? "misc.yml";
                    if (!grouped.ContainsKey(fileName))
                        grouped[fileName] = new List<Match>();
                    grouped[fileName].Add(kv.Value);
                }

                foreach (var (fileName, matches) in grouped)
                {
                    var targetPath = Path.Combine(AppSettings.KeywordsDir, fileName);
                    var group = new MatchGroup { Matches = matches };
                    _yamlWorkspace.WriteFileAsync(targetPath, group).GetAwaiter().GetResult();
                }

                AppSettings.SetDataFormatVersion(2);
                return true;
            }
            catch
            {
                if (File.Exists(AppSettings.DictBackupPath) && !File.Exists(AppSettings.DictPath))
                {
                    try { File.Copy(AppSettings.DictBackupPath, AppSettings.DictPath, overwrite: true); } catch { }
                }
                return false;
            }
        }
    }
}
