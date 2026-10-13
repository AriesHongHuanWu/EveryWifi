using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using EveryWifi.Core;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
    private static int Main()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "EveryWifi-Tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            BackendTests.Run(Check);
            var json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            var providers = LocalStore.ReadJson(Path.Combine(data, "providers.json"), new System.Collections.Generic.List<Provider>());
            Check(providers.Count >= 20, "genuine provider catalog available");
            Check(providers.All(p => p.ssids != null && p.passwords != null && NativeWifi.IsSafePortalUrl(p.sourceUrl)), "all catalog entries carry valid sources and arrays");
            Check(providers.Select(p => p.id).Distinct().Count() == providers.Count, "unique provider identifiers");
            Check(providers.Where(p => p.auth == "manual").All(p => p.ssids.Length == 0 && p.passwords.Length == 0), "manual guidance never guesses SSID or passwords");
            Check(providers.Where(p => p.auth == "portal" || p.auth == "open").All(p => p.passwords.Length == 0), "no invented portal passwords");
            var hotspots = LocalStore.ReadJson(Path.Combine(data, "hotspots.json"), new System.Collections.Generic.List<Hotspot>());
            Check(hotspots.Count >= 14000 && hotspots.Select(h => h.city).Distinct().Count() == 22, "official nationwide hotspot snapshot present");
            Check(hotspots.All(h => !string.IsNullOrWhiteSpace(h.name) && !string.IsNullOrWhiteSpace(h.address)), "hotspots have useful location records");
            var stores = LocalStore.ReadJson(Path.Combine(data, "louisa-stores.json"), new System.Collections.Generic.List<Store>());
            Check(stores.Count >= 500, "upstream stores preserved");
            var sample = new[] { new Provider { id = "my-cafe", name = "測試店家", auth = "manual", ssids = new[] { "公開網路" }, passwords = new string[0], sourceUrl = "https://example.com/" } };
            string file = Path.Combine(scratch, "custom.json"); LocalStore.WriteJson(file, sample); Check(LocalStore.ValidateImport(file).Count == 1, "unicode local catalog imports");
            sample[0].sourceUrl = "javascript:alert(1)"; LocalStore.WriteJson(file, sample); bool rejected = false; try { LocalStore.ValidateImport(file); } catch (ArgumentException) { rejected = true; } Check(rejected, "import rejects executable URL schemes");
            sample[0].sourceUrl = "https://example.com/"; sample[0].auth = "portal"; sample[0].passwords = new[] { "password" }; LocalStore.WriteJson(file, sample); rejected = false; try { LocalStore.ValidateImport(file); } catch (ArgumentException) { rejected = true; } Check(rejected, "import rejects password on portal item");
            sample[0].auth = "manual"; sample[0].passwords = new string[0]; sample[0].ssids = new[] { new string('店', 11) }; LocalStore.WriteJson(file, sample); rejected = false; try { LocalStore.ValidateImport(file); } catch (ArgumentException) { rejected = true; } Check(rejected, "import validates SSID UTF8 byte length");
            File.WriteAllText(file, "null"); Check(LocalStore.ReadJson(file, new AppSettings()) != null, "null settings fallback prevents startup crash");
            LocalStore.WriteJson(file, new { value = 1 }); LocalStore.WriteJson(file, new { value = 2 }); Check(File.ReadAllText(file).Contains("2"), "atomic settings replacement");
            byte[] secret = Encoding.UTF8.GetBytes("only-test-password-93842"); byte[] encrypted = ProtectedData.Protect(secret, Encoding.UTF8.GetBytes("EveryWifi.v1"), DataProtectionScope.CurrentUser); Check(!Encoding.UTF8.GetString(encrypted).Contains("only-test-password"), "DPAPI does not expose plaintext password"); Check(secret.SequenceEqual(ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes("EveryWifi.v1"), DataProtectionScope.CurrentUser)), "DPAPI current-user roundtrip");
            Console.WriteLine("PASS: " + checks + " checks. No scan/connect/disconnect, no startup registry or user settings changed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }
}
