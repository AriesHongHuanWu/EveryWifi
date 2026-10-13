using System;
using System.Collections.Generic;
namespace EveryWifi.Core
{
    public sealed class WifiNetwork
    {
        public string Ssid { get; set; }
        public byte[] SsidBytes { get; set; }
        public string Bssid { get; set; }
        public Guid InterfaceId { get; set; }
        public uint Signal { get; set; }
        public bool Secure { get; set; }
        public string Authentication { get; set; }
        public string Cipher { get; set; }
        public bool Connected { get; set; }
    }
    public sealed class WifiState
    {
        public bool Connected { get; set; }
        public string Ssid { get; set; }
        public string Bssid { get; set; }
        public string InterfaceName { get; set; }
        public Guid InterfaceId { get; set; }
        public uint Signal { get; set; }
        public uint RxMbps { get; set; }
        public uint TxMbps { get; set; }
        public string IpAddress { get; set; }
    }
    public sealed class InternetResult
    {
        public string Status { get; set; }
        public string Detail { get; set; }
        public string PortalUrl { get; set; }
    }
    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ProfileName { get; set; }
    }
    public sealed class Provider
    {
        public string id { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public string auth { get; set; }
        public string[] ssids { get; set; }
        public string[] passwords { get; set; }
        public string sourceUrl { get; set; }
        public string sourceLabel { get; set; }
        public string verifiedOn { get; set; }
        public string note { get; set; }
    }
    public sealed class Store
    {
        public string name { get; set; }
        public string address { get; set; }
        public string phone { get; set; }
        public double lat { get; set; }
        public double lng { get; set; }
        public string[] passwords { get; set; }
    }
}
