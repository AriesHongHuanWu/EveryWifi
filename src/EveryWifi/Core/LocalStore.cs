using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace EveryWifi.Core
{
    public sealed class AppSettings
    {
        public bool ConsentCompleted { get; set; }
        public bool AllowScan { get; set; }
        public bool AllowSave { get; set; }
        public bool AllowInternetCheck { get; set; }
        public bool AutoConnect { get; set; }
        public bool StartMinimized { get; set; }
        public bool Notifications { get; set; } = true;
    }
    public sealed class RememberedNetwork
    {
        public string Ssid { get; set; }
        public string Bssid { get; set; }
        public byte[] SsidBytes { get; set; }
        public string Password { get; set; }
        public string Authentication { get; set; }
        public string Cipher { get; set; }
        public string ProfileName { get; set; }
        public Guid InterfaceId { get; set; }
        public DateTime SavedAt { get; set; }
    }
    public sealed class HistoryEntry
    {
        public DateTime Time { get; set; }
        public string Network { get; set; }
        public string Result { get; set; }
    }
    public sealed class Hotspot
    {
        public string name { get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public double lat { get; set; }
        public double lng { get; set; }
        public string sourceUrl { get; set; }
    }
    public static class LocalStore
    {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EveryWifi");
        private static JavaScriptSerializer Json => new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        public static T ReadJson<T>(string file, T fallback)
        {
            try { var parsed = Json.Deserialize<T>(File.ReadAllText(file, Encoding.UTF8)); return ReferenceEquals(parsed, null) ? fallback : parsed; }
            catch (IOException) { return fallback; }
            catch (UnauthorizedAccessException) { return fallback; }
            catch (ArgumentException) { return fallback; }
            catch (InvalidOperationException) { return fallback; }
        }
        public static void WriteJson(string file, object data) => WriteAtomic(file, Encoding.UTF8.GetBytes(Json.Serialize(data)));
        public static void WriteAtomic(string file, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static AppSettings LoadSettings() => ReadJson(Path.Combine(DirectoryPath, "settings.json"), new AppSettings());
        public static void SaveSettings(AppSettings settings)
        {
            if (!settings.AllowScan || !settings.AllowSave) settings.AutoConnect = false;
            WriteJson(Path.Combine(DirectoryPath, "settings.json"), settings);
        }
        public static List<RememberedNetwork> LoadRemembered()
        {
            string path = Path.Combine(DirectoryPath, "saved-networks.dat");
            try
            {
                if (!File.Exists(path)) return new List<RememberedNetwork>();
                byte[] plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes("EveryWifi.v1"), DataProtectionScope.CurrentUser);
                return Json.Deserialize<List<RememberedNetwork>>(Encoding.UTF8.GetString(plaintext)) ?? new List<RememberedNetwork>();
            }
            catch (CryptographicException) { return new List<RememberedNetwork>(); }
            catch (IOException) { return new List<RememberedNetwork>(); }
            catch (UnauthorizedAccessException) { return new List<RememberedNetwork>(); }
            catch (ArgumentException) { return new List<RememberedNetwork>(); }
            catch (InvalidOperationException) { return new List<RememberedNetwork>(); }
        }
        public static void SaveRemembered(List<RememberedNetwork> networks)
        {
            byte[] plain = Encoding.UTF8.GetBytes(Json.Serialize(networks));
            byte[] encrypted = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("EveryWifi.v1"), DataProtectionScope.CurrentUser);
            WriteAtomic(Path.Combine(DirectoryPath, "saved-networks.dat"), encrypted);
        }
        public static List<Provider> LoadProviders()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            var builtIn = ReadJson(Path.Combine(root, "providers.json"), new List<Provider>());
            var custom = ReadJson(Path.Combine(DirectoryPath, "custom-providers.json"), new List<Provider>());
            return builtIn.Concat(custom).Where(p => p != null && !string.IsNullOrWhiteSpace(p.name)).ToList();
        }
        public static List<Provider> ValidateImport(string path)
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new ArgumentException("檔案上限為 2 MB。");
            var list = Json.Deserialize<List<Provider>>(File.ReadAllText(path, Encoding.UTF8));
            if (list == null || list.Count == 0 || list.Count > 200) throw new ArgumentException("請提供 1–200 個網路選項的 JSON 陣列。");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in list)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id) || p.id.Length > 80 || string.IsNullOrWhiteSpace(p.name) || p.name.Length > 120 || !ids.Add(p.id)) throw new ArgumentException("每個選項需要唯一 id 和名稱。");
                if (!new[] { "open", "portal", "store-password", "manual" }.Contains(p.auth)) throw new ArgumentException("auth 必須是 open、portal、store-password 或 manual。");
                p.ssids = p.ssids ?? new string[0]; p.passwords = p.passwords ?? new string[0];
                if (p.ssids.Length > 16 || p.ssids.Any(s => string.IsNullOrWhiteSpace(s) || Encoding.UTF8.GetByteCount(s) > 32 || s.IndexOf('\0') >= 0)) throw new ArgumentException("SSID 必須介於 1–32 UTF-8 bytes。");
                if (p.passwords.Length > 4 || p.passwords.Any(s => s == null || s.Length < 8 || s.Length > 64)) throw new ArgumentException("公開密碼須為 8–64 字元，每項最多 4 組；不會自動依序嘗試。");
                if ((p.auth == "open" || p.auth == "portal") && p.passwords.Length != 0) throw new ArgumentException("開放網路或網頁登入項目不可同時提供密碼。");
                if (!string.IsNullOrEmpty(p.sourceUrl) && !NativeWifi.IsSafePortalUrl(p.sourceUrl)) throw new ArgumentException("來源網址必須是有效 HTTP/HTTPS 網址。");
                if ((p.note ?? "").Length > 2000) throw new ArgumentException("說明長度上限 2000 字元。");
            }
            return list;
        }
        public static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key?.GetValue("EveryWifi") != null;
        }
        public static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("EveryWifi", "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray");
                else key.DeleteValue("EveryWifi", false);
            }
        }
    }
}
