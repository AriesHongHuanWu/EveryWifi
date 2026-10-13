using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using EveryWifi.Core;

public static class BackendTests
{
    private static int assertions;
    private static Action<bool, string> sink;
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
    public static void Run(Action<bool, string> check)
    {
        assertions = 0;
        sink = check;
        ProfileTests(); PortalTests(); NativeLayoutTests();
        Console.WriteLine("PASS: " + assertions + " backend assertions; no WLAN APIs invoked, no network settings changed.");
    }
    private static WifiNetwork Network(string ssid, string auth = "WPA2PSK", string cipher = "AES", bool secure = true)
    { return new WifiNetwork { Ssid = ssid, Authentication = auth, Cipher = cipher, Secure = secure }; }
    private static void ProfileTests()
    {
        var network = Network("咖啡&<店>\"WiFi");
        var profile = XElement.Parse(NativeWifi.BuildProfileXml(network, "A&B<pass>"));
        Equal(profile.Name, Ns + "WLANProfile", "correct WLAN namespace");
        Equal(profile.Element(Ns + "connectionMode").Value, "manual", "never autoconnect");
        Equal(profile.Element(Ns + "autoSwitch").Value, "false", "never autoswitch");
        Equal(profile.Descendants(Ns + "hex").Single().Value, BitConverter.ToString(Encoding.UTF8.GetBytes(network.Ssid)).Replace("-", ""), "UTF8 SSID exact bytes");
        Equal(profile.Descendants(Ns + "keyMaterial").Single().Value, "A&B<pass>", "escaped password roundtrip");
        True(profile.Element(Ns + "name").Value.StartsWith("EveryWifi-"), "namespaced profile");
        Equal(profile.Descendants(Ns + "authentication").Single().Value, "WPA2PSK", "WPA2 secure authentication");
        Equal(profile.Descendants(Ns + "encryption").Single().Value, "AES", "no encryption downgrade");
        network.SsidBytes = new byte[] { 0xff, 0x00, 0x26, 0x80 };
        profile = XElement.Parse(NativeWifi.BuildProfileXml(network, "password"));
        Equal(profile.Descendants(Ns + "hex").Single().Value, "FF002680", "raw SSID survives invalid UTF8 and NUL");
        var open = XElement.Parse(NativeWifi.BuildProfileXml(Network("iTaiwan", "open", "none", false), null));
        True(!open.Descendants(Ns + "sharedKey").Any(), "open profile no credentials");
        Equal(open.Descendants(Ns + "authentication").Single().Value, "open", "open auth explicit");
        var hex = XElement.Parse(NativeWifi.BuildProfileXml(Network("Personal"), new string('a', 64)));
        Equal(hex.Descendants(Ns + "keyType").Single().Value, "networkKey", "64 digit PSK is raw networkKey");
        var sae = XElement.Parse(NativeWifi.BuildProfileXml(Network("Personal", "WPA3SAE"), "password"));
        Equal(sae.Descendants(Ns + "authentication").Single().Value, "WPA3SAE", "SAE profile without WPA2 fallback");
        Reject(() => NativeWifi.BuildProfileXml(Network("Personal", "WPA3SAE"), new string('a', 64)), "SAE does not accept raw WPA2 key");
        Reject(() => NativeWifi.BuildProfileXml(Network("Enterprise", "WPA2-Enterprise"), "password"), "enterprise not guessed");
        Reject(() => NativeWifi.BuildProfileXml(Network("Legacy", "WPA2PSK", "TKIP"), "password"), "TKIP not downgraded");
        Reject(() => NativeWifi.BuildProfileXml(Network("Unexpected", "open", "AES", false), "password"), "inconsistent metadata rejected");
        Reject(() => NativeWifi.BuildProfileXml(Network("iTaiwan", "open", "none", false), "password"), "portal password not put in WLAN profile");
        Reject(() => NativeWifi.BuildProfileXml(Network("Personal"), "short"), "short PSK rejected");
        Reject(() => NativeWifi.BuildProfileXml(Network("Personal"), new string('z', 64)), "nonhex 64 key rejected");
        Reject(() => NativeWifi.BuildProfileXml(Network("Personal"), "password\n"), "PSK control characters rejected");
        Reject(() => NativeWifi.BuildProfileXml(Network("Personal"), "密碼密碼密碼密碼"), "unsupported nonASCII PSK rejected");
        Reject(() => NativeWifi.BuildProfileXml(Network(new string('a', 33)), "password"), "SSID limit is bytes");
        Reject(() => NativeWifi.BuildProfileXml(Network(new string('店', 11)), "password"), "UTF8 byte limit");
        Reject(() => NativeWifi.BuildProfileXml(Network(""), "password"), "hidden empty SSID rejected");
        var first = XElement.Parse(NativeWifi.BuildProfileXml(Network("Same"), "password")).Element(Ns + "name").Value;
        var repeat = XElement.Parse(NativeWifi.BuildProfileXml(Network("Same"), "anotherpass")).Element(Ns + "name").Value;
        var other = XElement.Parse(NativeWifi.BuildProfileXml(Network("Same", "WPA3SAE"), "password")).Element(Ns + "name").Value;
        Equal(first, repeat, "stable profile per SSID and security");
        True(first != other, "different security yields different profile");
        var router = Network("LouisaCoffee"); router.Bssid = "00:11:22:33:44:55";
        string router1 = XElement.Parse(NativeWifi.BuildProfileXml(router, "password")).Element(Ns + "name").Value;
        router.Bssid = "00-11-22-33-44-55";
        Equal(XElement.Parse(NativeWifi.BuildProfileXml(router, "differentpass")).Element(Ns + "name").Value, router1, "BSSID representation canonical and password-independent");
        router.Bssid = "00:11:22:33:44:66";
        True(XElement.Parse(NativeWifi.BuildProfileXml(router, "password")).Element(Ns + "name").Value != router1, "same-name different shop/router has separate profile");
        router.Bssid = "invalid";
        Reject(() => NativeWifi.BuildProfileXml(router, "password"), "invalid BSSID rejected");
        using (var native = new NativeWifi())
            True(!native.DeleteProfile(Guid.NewGuid(), "My home Wi-Fi").Success, "refuse unrelated profile deletion before any native call");
    }
    private static void PortalTests()
    {
        foreach (string value in new[] { "https://wifi.example.tw/login?a=1", "http://192.168.1.1:8080/login", "https://example.tw/登入" })
            True(NativeWifi.IsSafePortalUrl(value), "allow ordinary http(s) portal " + value);
        foreach (string value in new[] { "javascript:alert(1)", "file:///C:/test", "ms-settings:privacy-location", "https://user:secret@example.tw/", "http://localhost/", "http://127.0.0.1/", "http://[::1]/", "http://x.localhost/", "https://example.tw/\r\nHeader:value", "https://example.tw\\@evil.tw/", "//example.tw/", "", null })
            True(!NativeWifi.IsSafePortalUrl(value), "reject unsafe or unsupported portal " + value);
    }
    private static void NativeLayoutTests()
    {
        Layout("Dot11Ssid", 36); Layout("InterfaceInfo", 532); Layout("AvailableNetwork", 628);
        Layout("RateSet", 256); Layout("BssEntry", 360); Layout("AssociationAttributes", 68);
        Layout("SecurityAttributes", 16); Layout("ConnectionAttributes", 604); Layout("BssidList", 20); Layout("ProfileInfo", 516);
        Layout("NotificationData", IntPtr.Size == 8 ? 40 : 32); Layout("ConnectionParameters", IntPtr.Size == 8 ? 40 : 24);
        var bss = typeof(NativeWifi).GetNestedType("BssEntry", BindingFlags.NonPublic);
        Equal(Marshal.OffsetOf(bss, "Timestamp").ToInt32(), 72, "BSS ULONGLONG alignment");
        Equal(Marshal.OffsetOf(bss, "Capabilities").ToInt32(), 88, "BSS capability offset");
        Equal(Marshal.OffsetOf(bss, "Rates").ToInt32(), 96, "BSS rate set offset");
    }
    private static void Layout(string name, int expected)
    { Equal(Marshal.SizeOf(typeof(NativeWifi).GetNestedType(name, BindingFlags.NonPublic)), expected, "native layout " + name); }
    private static void Reject(Action action, string message)
    { try { action(); } catch (ArgumentException) { assertions++; if (sink != null) sink(true, message); return; } if (sink != null) sink(false, message); throw new Exception("Expected rejection: " + message); }
    private static void True(bool condition, string message)
    { assertions++; if (sink != null) sink(condition, message); if (!condition) throw new Exception("Failed: " + message); }
    private static void Equal<T>(T actual, T expected, string message)
    { assertions++; bool good = Equals(actual, expected); if (sink != null) sink(good, message); if (!good) throw new Exception("Failed: " + message + "; expected " + expected + ", actual " + actual); }
}
