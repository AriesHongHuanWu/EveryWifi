using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EveryWifi.Core
{
    /// <summary>Uses Windows WLAN APIs only; no shell, service installation, or elevated privilege.</summary>
    public sealed class NativeWifi : IDisposable
    {
        private const uint UserProfile = 2;
        private const uint NotificationAcm = 8;
        private const string ProfilePrefix = "EveryWifi-";
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> scans = new ConcurrentDictionary<Guid, TaskCompletionSource<bool>>();
        private readonly NotificationCallback callback;
        private IntPtr handle;
        private volatile bool disposed;
        private bool notificationsAvailable;
        private uint notificationError;

        public NativeWifi() { callback = OnNotification; }

        public async Task<List<WifiNetwork>> ScanAsync()
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                EnsureOpen();
                if (!notificationsAvailable)
                    throw new InvalidOperationException("Windows 無法提供 Wi-Fi 掃描完成通知（錯誤 " + notificationError + "）；無法確認附近清單是否已更新。請重新啟動 EveryWifi，或使用 Windows Wi-Fi 設定。");
                var interfaces = EnumerateInterfaces().Where(i => i.State != 0).ToList();
                if (interfaces.Count == 0) throw new InvalidOperationException("找不到可用的 Wi-Fi 網卡。請確認 Wi-Fi 已開啟，且 WLAN AutoConfig 服務正在執行。");
                var waits = new List<Task>();
                uint firstError = 0;
                foreach (var item in interfaces)
                {
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    scans[item.Id] = completion;
                    var id = item.Id;
                    uint code = WlanScan(handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (code == 5) ThrowError(code, "掃描 Wi-Fi", true);
                    if (code != 0)
                    {
                        if (firstError == 0) firstError = code;
                        TaskCompletionSource<bool> ignored;
                        scans.TryRemove(id, out ignored);
                        continue;
                    }
                    // WlanScan is asynchronous. Wait for ACM completion, bounded for faulty drivers.
                    waits.Add(Task.WhenAny(completion.Task, Task.Delay(6500, lifetime.Token)));
                }
                if (waits.Count == 0) ThrowError(firstError, "掃描 Wi-Fi", false);
                await Task.WhenAll(waits).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                interfaces = interfaces.Where(i =>
                {
                    TaskCompletionSource<bool> completion;
                    return scans.TryGetValue(i.Id, out completion) && completion.Task.Status == TaskStatus.RanToCompletion && completion.Task.Result;
                }).ToList();
                if (interfaces.Count == 0) throw new InvalidOperationException("Windows Wi-Fi 掃描未完成或已逾時。請確認 Wi-Fi 已開啟，再重新掃描。");
                return await Task.Run(() => ReadNetworks(interfaces), lifetime.Token).ConfigureAwait(false);
            }
            finally { scans.Clear(); gate.Release(); }
        }

        public async Task<WifiState> GetStateAsync()
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                EnsureOpen();
                return await Task.Run(() => ReadState(), lifetime.Token).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }

        public async Task<OperationResult> ConnectAsync(WifiNetwork network, string password)
        {
            string xml;
            string profile;
            byte[] ssid;
            byte[] bssid;
            try
            {
                xml = BuildProfileXml(network, password);
                ssid = GetSsidBytes(network);
                bssid = ParseBssid(network.Bssid);
                profile = ProfileName(network, ssid);
                if (network.InterfaceId == Guid.Empty) throw new ArgumentException("請先選擇掃描到的 Wi-Fi 網卡。");
            }
            catch (ArgumentException ex) { return Failure(ex.Message); }

            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                EnsureOpen();
                var id = network.InterfaceId;
                uint reason;
                uint code = WlanSetProfile(handle, ref id, UserProfile, xml, null, true, IntPtr.Zero, out reason);
                if (code != 0) return Failure(ErrorMessage(code, "儲存使用者 Wi-Fi 設定", false) + ReasonMessage(reason), profile);
                IntPtr ssidPtr = IntPtr.Zero;
                IntPtr bssidPtr = IntPtr.Zero;
                try
                {
                    var nativeSsid = new Dot11Ssid { Length = (uint)ssid.Length, Bytes = new byte[32] };
                    Buffer.BlockCopy(ssid, 0, nativeSsid.Bytes, 0, ssid.Length);
                    ssidPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Dot11Ssid)));
                    Marshal.StructureToPtr(nativeSsid, ssidPtr, false);
                    if (bssid != null)
                    {
                        var target = new BssidList { Type = 0x80, Revision = 1, Size = 20, Count = 1, TotalCount = 1, Bssid = bssid };
                        bssidPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(BssidList)));
                        Marshal.StructureToPtr(target, bssidPtr, false);
                    }
                    var parameters = new ConnectionParameters { Mode = 0, Profile = profile, Ssid = ssidPtr, Bssids = bssidPtr, BssType = 1, Flags = 0 };
                    code = WlanConnect(handle, ref id, ref parameters, IntPtr.Zero);
                }
                finally
                {
                    if (ssidPtr != IntPtr.Zero) Marshal.FreeHGlobal(ssidPtr);
                    if (bssidPtr != IntPtr.Zero) Marshal.FreeHGlobal(bssidPtr);
                }
                if (code != 0) return Failure(ErrorMessage(code, "連線 Wi-Fi", false), profile);

                // Accepting WlanConnect does not prove association. Confirm actual interface state.
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    await Task.Delay(500, lifetime.Token).ConfigureAwait(false);
                    ConnectionAttributes? current = QueryConnection(id);
                    if (!current.HasValue || current.Value.State != 1 || !string.Equals(current.Value.Profile, profile, StringComparison.Ordinal)) continue;
                    byte[] actualSsid = CopySsid(current.Value.Association.Ssid);
                    if (!actualSsid.SequenceEqual(ssid)) continue;
                    if (bssid != null && !current.Value.Association.Bssid.SequenceEqual(bssid))
                        return Failure("Windows 已連到同名 Wi-Fi，但基地台識別碼不同；請確認商家網路。EveryWifi 沒有將它標示為已確認連線。", profile);
                    return new OperationResult { Success = true, ProfileName = profile, Message = "已確認 Wi-Fi 連線。網際網路與登入頁狀態需要另外檢查。" };
                }
                return Failure("Windows 已接受連線要求，但 20 秒內未確認連線；請查看連線狀態或 Windows Wi-Fi 設定。可能是密碼錯誤、訊號不足，或網卡尚在處理。", profile);
            }
            catch (Win32Exception ex) { return Failure(ex.Message, profile); }
            finally { gate.Release(); }
        }

        public async Task<OperationResult> DisconnectAsync(Guid interfaceId)
        {
            if (interfaceId == Guid.Empty) return Failure("沒有選擇 Wi-Fi 網卡。");
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                EnsureOpen();
                uint code = WlanDisconnect(handle, ref interfaceId, IntPtr.Zero);
                if (code != 0) return Failure(ErrorMessage(code, "中斷 Wi-Fi", false));
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    await Task.Delay(250, lifetime.Token).ConfigureAwait(false);
                    if (!QueryConnection(interfaceId).HasValue)
                        return new OperationResult { Success = true, Message = "已確認 Wi-Fi 中斷。" };
                }
                return Failure("Windows 已接受中斷要求，但尚未確認中斷。請重新查看狀態。");
            }
            catch (Win32Exception ex) { return Failure(ex.Message); }
            finally { gate.Release(); }
        }

        public OperationResult DeleteProfile(Guid interfaceId, string profileName)
        {
            // Never delete a user's unrelated saved Wi-Fi profiles.
            if (interfaceId == Guid.Empty || string.IsNullOrEmpty(profileName) || !profileName.StartsWith(ProfilePrefix, StringComparison.Ordinal))
                return Failure("只允許移除 EveryWifi 建立的 Wi-Fi 設定。");
            gate.Wait(lifetime.Token);
            try
            {
                EnsureOpen();
                uint code = WlanDeleteProfile(handle, ref interfaceId, profileName, IntPtr.Zero);
                if (code == 0 || code == 1168) return new OperationResult { Success = true, Message = "EveryWifi Wi-Fi 設定已移除。", ProfileName = profileName };
                return Failure(ErrorMessage(code, "移除 Wi-Fi 設定", false), profileName);
            }
            catch (Win32Exception ex) { return Failure(ex.Message, profileName); }
            finally { gate.Release(); }
        }

        public OperationResult DeleteAllAppProfiles()
        {
            gate.Wait(lifetime.Token);
            try
            {
                EnsureOpen();
                int deleted = 0;
                var failures = new List<string>();
                foreach (var adapter in EnumerateInterfaces())
                {
                    var id = adapter.Id;
                    IntPtr buffer;
                    uint code = WlanGetProfileList(handle, ref id, IntPtr.Zero, out buffer);
                    if (code != 0) { failures.Add(ErrorMessage(code, "讀取 Wi-Fi 設定", false)); continue; }
                    try
                    {
                        int count = Marshal.ReadInt32(buffer);
                        if (count < 0 || count > 65536) throw new InvalidOperationException("Windows 回傳無效的 Wi-Fi 設定數量。");
                        int size = Marshal.SizeOf(typeof(ProfileInfo));
                        for (int i = 0; i < count; i++)
                        {
                            var profile = (ProfileInfo)Marshal.PtrToStructure(IntPtr.Add(buffer, 8 + i * size), typeof(ProfileInfo));
                            if ((profile.Flags & UserProfile) == 0 || !profile.Name.StartsWith(ProfilePrefix, StringComparison.Ordinal)) continue;
                            code = WlanDeleteProfile(handle, ref id, profile.Name, IntPtr.Zero);
                            if (code == 0) deleted++;
                            else if (code != 1168) failures.Add(ErrorMessage(code, "移除 EveryWifi Wi-Fi 設定", false));
                        }
                    }
                    finally { WlanFreeMemory(buffer); }
                }
                return new OperationResult { Success = failures.Count == 0,
                    Message = "已移除 " + deleted + " 個 EveryWifi 使用者 Wi-Fi 設定。" + (failures.Count == 0 ? "" : "\n" + string.Join("\n", failures.Distinct())) };
            }
            catch (Win32Exception ex) { return Failure(ex.Message); }
            finally { gate.Release(); }
        }

        public static string BuildProfileXml(WifiNetwork network, string password)
        {
            if (network == null) throw new ArgumentNullException("network");
            byte[] ssid = GetSsidBytes(network);
            string auth = network.Authentication ?? "";
            string cipher = network.Cipher ?? "";
            bool open = !network.Secure && auth.Equals("open", StringComparison.OrdinalIgnoreCase) && cipher.Equals("none", StringComparison.OrdinalIgnoreCase);
            bool wpa2 = network.Secure && auth.Equals("WPA2PSK", StringComparison.OrdinalIgnoreCase) && cipher.Equals("AES", StringComparison.OrdinalIgnoreCase);
            bool wpa3 = network.Secure && auth.Equals("WPA3SAE", StringComparison.OrdinalIgnoreCase) && cipher.Equals("AES", StringComparison.OrdinalIgnoreCase);
            if (!open && !wpa2 && !wpa3)
                throw new ArgumentException("這個安全性類型需要 Windows Wi-Fi 設定處理。EveryWifi 支援開放網路、WPA2-Personal AES 與 WPA3-Personal AES；不會降級加密或猜測企業登入設定。");
            if (open && !string.IsNullOrEmpty(password)) throw new ArgumentException("開放網路沒有 Wi-Fi 密碼；登入頁的帳號密碼請在商家登入頁輸入。");
            bool rawKey = false;
            if (!open)
            {
                password = password ?? "";
                rawKey = wpa2 && password.Length == 64 && password.All(IsHex);
                if (!rawKey && (password.Length < 8 || password.Length > 63 || password.Any(c => c < 0x20 || c > 0x7e)))
                    throw new ArgumentException(wpa3
                        ? "WPA3 密碼請輸入 8–63 個可列印的英數字或符號；不接受 WPA2 的 64 位十六進位金鑰。"
                        : "WPA2 密碼請輸入 8–63 個可列印的英數字或符號，或 64 位十六進位金鑰。");
            }
            XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
            var security = new XElement(ns + "security",
                new XElement(ns + "authEncryption", new XElement(ns + "authentication", open ? "open" : wpa3 ? "WPA3SAE" : "WPA2PSK"),
                    new XElement(ns + "encryption", open ? "none" : "AES"), new XElement(ns + "useOneX", "false")));
            if (!open) security.Add(new XElement(ns + "sharedKey", new XElement(ns + "keyType", rawKey ? "networkKey" : "passPhrase"),
                new XElement(ns + "protected", "false"), new XElement(ns + "keyMaterial", password)));
            return new XElement(ns + "WLANProfile", new XElement(ns + "name", ProfileName(network, ssid)),
                new XElement(ns + "SSIDConfig", new XElement(ns + "SSID", new XElement(ns + "hex", ToHex(ssid))), new XElement(ns + "nonBroadcast", "false")),
                new XElement(ns + "connectionType", "ESS"), new XElement(ns + "connectionMode", "manual"),
                new XElement(ns + "autoSwitch", "false"), new XElement(ns + "MSM", security)).ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>Protocol validation only; a safe URL is not an endorsement of its operator.</summary>
        public static bool IsSafePortalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || url.Any(c => char.IsControl(c) || c == '\\')) return false;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host) || uri.IsLoopback) return false;
            string host = uri.IdnHost.TrimEnd('.');
            return !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureOpen()
        {
            if (disposed) throw new ObjectDisposedException("NativeWifi");
            if (handle != IntPtr.Zero) return;
            uint version;
            ThrowError(WlanOpenHandle(2, IntPtr.Zero, out version, out handle), "開啟 Windows Wi-Fi", false);
            uint oldSource;
            // ACM notifications do not request the MSM location capability.
            notificationError = WlanRegisterNotification(handle, NotificationAcm, false, callback, IntPtr.Zero, IntPtr.Zero, out oldSource);
            notificationsAvailable = notificationError == 0;
        }

        private void OnNotification(ref NotificationData data, IntPtr context)
        {
            try
            {
                if ((data.Source & NotificationAcm) == 0 || (data.Code != 7 && data.Code != 8)) return;
                TaskCompletionSource<bool> completion;
                if (scans.TryGetValue(data.InterfaceId, out completion)) completion.TrySetResult(data.Code == 7);
            }
            catch { /* Exceptions must never cross the unmanaged callback boundary. */ }
        }

        private List<InterfaceInfo> EnumerateInterfaces()
        {
            IntPtr buffer;
            ThrowError(WlanEnumInterfaces(handle, IntPtr.Zero, out buffer), "讀取 Wi-Fi 網卡", false);
            try
            {
                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > 1024) throw new InvalidOperationException("Windows 回傳無效的 Wi-Fi 網卡數量。");
                var result = new List<InterfaceInfo>();
                int size = Marshal.SizeOf(typeof(InterfaceInfo));
                for (int i = 0; i < count; i++) result.Add((InterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(buffer, 8 + i * size), typeof(InterfaceInfo)));
                return result;
            }
            finally { WlanFreeMemory(buffer); }
        }

        private List<WifiNetwork> ReadNetworks(List<InterfaceInfo> interfaces)
        {
            var result = new List<WifiNetwork>();
            uint firstError = 0;
            int successfulInterfaces = 0;
            foreach (var item in interfaces)
            {
                var id = item.Id;
                IntPtr buffer;
                uint code = WlanGetAvailableNetworkList(handle, ref id, 0, IntPtr.Zero, out buffer);
                if (code == 5) ThrowError(code, "讀取附近 Wi-Fi", true);
                if (code != 0) { if (firstError == 0) firstError = code; continue; }
                successfulInterfaces++;
                try
                {
                    int count = Marshal.ReadInt32(buffer);
                    if (count < 0 || count > 65536) throw new InvalidOperationException("Windows 回傳無效的 Wi-Fi 網路數量。");
                    int size = Marshal.SizeOf(typeof(AvailableNetwork));
                    var bsses = ReadBsses(id);
                    ConnectionAttributes? current = item.State == 1 ? QueryConnection(id) : null;
                    for (int i = 0; i < count; i++)
                    {
                        var native = (AvailableNetwork)Marshal.PtrToStructure(IntPtr.Add(buffer, 8 + i * size), typeof(AvailableNetwork));
                        if (native.BssType != 1 || native.Ssid.Length == 0 || native.Ssid.Length > 32) continue;
                        byte[] bytes = CopySsid(native.Ssid);
                        var candidate = bsses.Where(b => b.BssType == 1 && CopySsid(b.Ssid).SequenceEqual(bytes)
                            && (((b.Capabilities & 0x10) != 0) == native.Security)).OrderByDescending(b => b.Quality).ToList();
                        string bssid = candidate.Count > 0 ? FormatMac(candidate[0].Bssid) : null;
                        bool connected = (native.Flags & 1) != 0;
                        if (connected && current.HasValue && CopySsid(current.Value.Association.Ssid).SequenceEqual(bytes))
                            bssid = FormatMac(current.Value.Association.Bssid);
                        result.Add(new WifiNetwork { Ssid = Encoding.UTF8.GetString(bytes), SsidBytes = bytes, Bssid = bssid,
                            InterfaceId = id, Signal = Math.Min(100u, native.Signal), Secure = native.Security,
                            Authentication = AuthName(native.Auth), Cipher = CipherName(native.Cipher), Connected = connected });
                    }
                }
                finally { WlanFreeMemory(buffer); }
            }
            if (successfulInterfaces == 0 && firstError != 0) ThrowError(firstError, "讀取附近 Wi-Fi", false);
            // Windows can expose several saved profiles for the same SSID. Keep one per security/adapter.
            return result.GroupBy(n => n.InterfaceId + "/" + ToHex(n.SsidBytes) + "/" + n.Authentication + "/" + n.Cipher)
                .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).First())
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ThenBy(n => n.Ssid).ToList();
        }

        private List<BssEntry> ReadBsses(Guid id)
        {
            IntPtr buffer;
            uint code = WlanGetNetworkBssList(handle, ref id, IntPtr.Zero, 3, false, IntPtr.Zero, out buffer);
            if (code == 5) ThrowError(code, "讀取 Wi-Fi 基地台", true);
            if (code != 0) return new List<BssEntry>();
            try
            {
                uint total = unchecked((uint)Marshal.ReadInt32(buffer));
                int count = Marshal.ReadInt32(buffer, 4);
                int size = Marshal.SizeOf(typeof(BssEntry));
                if (count < 0 || count > 65536 || total < 8 || (ulong)count * (uint)size + 8 > total)
                    throw new InvalidOperationException("Windows 回傳無效的 Wi-Fi 基地台清單。");
                var result = new List<BssEntry>();
                for (int i = 0; i < count; i++) result.Add((BssEntry)Marshal.PtrToStructure(IntPtr.Add(buffer, 8 + i * size), typeof(BssEntry)));
                return result;
            }
            finally { WlanFreeMemory(buffer); }
        }

        private WifiState ReadState()
        {
            var interfaces = EnumerateInterfaces();
            foreach (var item in interfaces)
            {
                if (item.State != 1) continue;
                var connection = QueryConnection(item.Id);
                if (!connection.HasValue || connection.Value.State != 1) continue;
                var a = connection.Value.Association;
                return new WifiState { Connected = true, Ssid = Encoding.UTF8.GetString(CopySsid(a.Ssid)),
                    Bssid = FormatMac(a.Bssid), InterfaceName = item.Description, InterfaceId = item.Id,
                    Signal = Math.Min(100u, a.Signal), RxMbps = a.RxRate / 1000, TxMbps = a.TxRate / 1000, IpAddress = FindIp(item.Id) };
            }
            return new WifiState { Connected = false, InterfaceId = interfaces.Count > 0 ? interfaces[0].Id : Guid.Empty,
                InterfaceName = interfaces.Count > 0 ? interfaces[0].Description : "沒有 Wi-Fi 網卡" };
        }

        private ConnectionAttributes? QueryConnection(Guid id)
        {
            IntPtr buffer;
            uint size;
            uint type;
            uint code = WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out size, out buffer, out type);
            if (code == 5023 || code == 1168) return null;
            ThrowError(code, "讀取 Wi-Fi 連線狀態", true);
            try
            {
                if (size < Marshal.SizeOf(typeof(ConnectionAttributes))) throw new InvalidOperationException("Windows 回傳無效的 Wi-Fi 連線狀態。");
                return (ConnectionAttributes)Marshal.PtrToStructure(buffer, typeof(ConnectionAttributes));
            }
            finally { WlanFreeMemory(buffer); }
        }

        private static string FindIp(Guid id)
        {
            foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
            {
                Guid parsed;
                if (!Guid.TryParse(item.Id, out parsed) || parsed != id) continue;
                return string.Join(", ", item.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()));
            }
            return "";
        }

        private static byte[] GetSsidBytes(WifiNetwork network)
        {
            if (network == null) throw new ArgumentNullException("network");
            byte[] bytes = network.SsidBytes != null ? (byte[])network.SsidBytes.Clone() : Encoding.UTF8.GetBytes(network.Ssid ?? "");
            if (bytes.Length == 0 || bytes.Length > 32) throw new ArgumentException("Wi-Fi 名稱必須是 1–32 個位元組；中文名稱以 UTF-8 位元組計算。");
            return bytes;
        }
        private static byte[] CopySsid(Dot11Ssid ssid)
        {
            if (ssid.Length > 32 || ssid.Bytes == null) throw new InvalidOperationException("無效的 SSID 位元組長度。");
            var result = new byte[ssid.Length];
            Buffer.BlockCopy(ssid.Bytes, 0, result, 0, result.Length);
            return result;
        }
        private static string ProfileName(WifiNetwork network, byte[] ssid)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bssid = ParseBssid(network.Bssid);
                byte[] suffix = Encoding.ASCII.GetBytes("/" + (network.Authentication ?? "").ToUpperInvariant() + "/" + (network.Cipher ?? "").ToUpperInvariant()
                    + "/" + (bssid == null ? "" : ToHex(bssid)));
                return ProfilePrefix + ToHex(sha.ComputeHash(ssid.Concat(suffix).ToArray())).Substring(0, 32);
            }
        }
        private static bool IsHex(char c) { return c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'; }
        private static string ToHex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
        private static string FormatMac(byte[] bytes) { return BitConverter.ToString(bytes).Replace('-', ':'); }
        private static byte[] ParseBssid(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string cleaned = value.Replace(":", "").Replace("-", "");
            if (cleaned.Length != 12 || !cleaned.All(IsHex)) throw new ArgumentException("Wi-Fi 基地台識別碼格式不正確；請重新掃描。");
            var result = new byte[6];
            for (int i = 0; i < result.Length; i++) result[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return result;
        }
        private static string AuthName(uint value)
        {
            switch (value) { case 1: return "open"; case 2: return "WEP"; case 3: return "WPA-Enterprise"; case 4: return "WPAPSK";
                case 5: return "WPA-NONE"; case 6: return "WPA2-Enterprise"; case 7: return "WPA2PSK"; case 8: return "WPA3-Enterprise-192";
                case 9: return "WPA3SAE"; case 10: return "OWE"; case 11: return "WPA3-Enterprise"; default: return "unknown-" + value; }
        }
        private static string CipherName(uint value)
        {
            switch (value) { case 0: return "none"; case 1: return "WEP40"; case 2: return "TKIP"; case 4: return "AES";
                case 5: return "WEP104"; case 8: return "GCMP"; case 9: return "GCMP256"; case 10: return "CCMP256"; case 0x101: return "WEP"; default: return "unknown-" + value; }
        }
        private static OperationResult Failure(string message, string profile = null) { return new OperationResult { Success = false, Message = message, ProfileName = profile }; }
        private static void ThrowError(uint code, string operation, bool locationSensitive)
        {
            if (code != 0) throw new Win32Exception((int)code, ErrorMessage(code, operation, locationSensitive));
        }
        private static string ErrorMessage(uint code, string operation, bool locationSensitive)
        {
            if (code == 5 && locationSensitive)
                return operation + "被 Windows 拒絕（錯誤 5）。新版 Windows 11 需要位置權限才能讀取附近 Wi-Fi／基地台；請在「設定 → 隱私權與安全性 → 位置」確認位置服務與應用程式權限（ms-settings:privacy-location）。公司政策也可能限制存取。";
            if (code == 1062) return "Windows WLAN AutoConfig 服務尚未啟動。請在 Windows 設定檢查 Wi-Fi 網卡與 WLAN 服務。";
            if (code == 5023) return operation + "暫時不可用。請確認 Wi-Fi 已開啟、飛航模式已關閉。";
            if (code == 5) return operation + "被 Windows 權限或公司政策拒絕。EveryWifi 不會自行提升為系統管理員。";
            return operation + "失敗（Windows 錯誤 " + code + "）：" + new Win32Exception((int)code).Message;
        }
        private static string ReasonMessage(uint reason)
        {
            if (reason == 0) return "";
            var text = new StringBuilder(512);
            return WlanReasonCodeToString(reason, 512, text, IntPtr.Zero) == 0 ? "\n" + text : "\n設定原因碼：" + reason;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            gate.Wait();
            try
            {
                if (handle != IntPtr.Zero)
                {
                    uint old;
                    WlanRegisterNotification(handle, 0, true, null, IntPtr.Zero, IntPtr.Zero, out old);
                    WlanCloseHandle(handle, IntPtr.Zero);
                    handle = IntPtr.Zero;
                }
            }
            finally { gate.Release(); }
            // Keep gate/token alive for any in-flight callers to finish their cancellation paths.
        }

        [StructLayout(LayoutKind.Sequential)] private struct Dot11Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct InterfaceInfo { public Guid Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public uint State; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProfileInfo { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name; public uint Flags; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AvailableNetwork
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
            public Dot11Ssid Ssid; public uint BssType; public uint BssidCount;
            [MarshalAs(UnmanagedType.Bool)] public bool Connectable;
            public uint NotConnectableReason; public uint PhyCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
            [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
            public uint Signal; [MarshalAs(UnmanagedType.Bool)] public bool Security;
            public uint Auth; public uint Cipher; public uint Flags; public uint Reserved;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RateSet { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates; }
        [StructLayout(LayoutKind.Sequential)] private struct BssEntry
        {
            public Dot11Ssid Ssid; public uint PhyId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public uint BssType; public uint PhyType; public int Rssi; public uint Quality;
            [MarshalAs(UnmanagedType.U1)] public bool InRegDomain;
            public ushort BeaconPeriod; public ulong Timestamp; public ulong HostTimestamp;
            public ushort Capabilities; public uint Frequency; public RateSet Rates; public uint IeOffset; public uint IeSize;
        }
        [StructLayout(LayoutKind.Sequential)] private struct AssociationAttributes
        {
            public Dot11Ssid Ssid; public uint BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
            public uint PhyType; public uint PhyIndex; public uint Signal; public uint RxRate; public uint TxRate;
        }
        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes
        {
            [MarshalAs(UnmanagedType.Bool)] public bool Security; [MarshalAs(UnmanagedType.Bool)] public bool OneX;
            public uint Auth; public uint Cipher;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ConnectionAttributes
        {
            public uint State; public uint Mode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
            public AssociationAttributes Association; public SecurityAttributes Security;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ConnectionParameters
        {
            public uint Mode; [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
            public IntPtr Ssid; public IntPtr Bssids; public uint BssType; public uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BssidList
        {
            public byte Type; public byte Revision; public ushort Size; public uint Count; public uint TotalCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NotificationData { public uint Source; public uint Code; public Guid InterfaceId; public uint Size; public IntPtr Data; }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void NotificationCallback(ref NotificationData data, IntPtr context);

        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiatedVersion, out IntPtr client);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr client, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr client, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr buffer);
        [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr client, ref Guid id, IntPtr ssid, IntPtr ieData, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr client, ref Guid id, uint flags, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetNetworkBssList(IntPtr client, ref Guid id, IntPtr ssid, uint bssType, [MarshalAs(UnmanagedType.Bool)] bool security, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr client, ref Guid id, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint valueType);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanSetProfile(IntPtr client, ref Guid id, uint flags, string xml, string security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reason);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanDeleteProfile(IntPtr client, ref Guid id, string profileName, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetProfileList(IntPtr client, ref Guid id, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanConnect(IntPtr client, ref Guid id, ref ConnectionParameters parameters, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanDisconnect(IntPtr client, ref Guid id, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(IntPtr client, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, NotificationCallback callback, IntPtr context, IntPtr reserved, out uint previousSource);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanReasonCodeToString(uint reason, uint size, StringBuilder text, IntPtr reserved);
    }

    public sealed class InternetProbe
    {
        public const string Endpoint = "http://www.msftconnecttest.com/connecttest.txt";
        public async Task<InternetResult> CheckAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return Result("offline", "Windows 沒有可用的網路介面。", null);
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) })
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
            {
                try
                {
                    using (var response = await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status <= 399)
                        {
                            Uri location = response.Headers.Location;
                            string target = location == null ? null : (location.IsAbsoluteUri ? location : new Uri(new Uri(Endpoint), location)).AbsoluteUri;
                            return Result("captive", "偵測到重新導向；可能需要商家登入或同意條款。", NativeWifi.IsSafePortalUrl(target) ? target : null);
                        }
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                var bytes = new byte[256];
                                int length = 0;
                                while (length < bytes.Length)
                                {
                                    int read = await stream.ReadAsync(bytes, length, bytes.Length - length, timeout.Token).ConfigureAwait(false);
                                    if (read == 0) break;
                                    length += read;
                                }
                                string content = Encoding.UTF8.GetString(bytes, 0, length).TrimEnd('\r', '\n');
                                return content == "Microsoft Connect Test"
                                    ? Result("online", "Microsoft 連線測試回應正確。此結果不代表所有網站都可存取。", null)
                                    : Result("captive", "測試內容被替換；可能是登入頁、代理伺服器或網路限制。", null);
                            }
                        }
                        return Result("unknown", "Microsoft 連線測試回傳 HTTP " + status + "；無法確認網際網路狀態。", null);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is UriFormatException)
                {
                    return NetworkInterface.GetIsNetworkAvailable()
                        ? Result("unknown", "連線測試未成功；可能是逾時、DNS、代理伺服器或端點受阻。", null)
                        : Result("offline", "Windows 沒有可用的網路介面。", null);
                }
            }
        }
        private static InternetResult Result(string status, string detail, string portal) { return new InternetResult { Status = status, Detail = detail, PortalUrl = portal }; }
    }
}
