# Windows WLAN backend

EveryWifi uses the operating system's `wlanapi.dll` directly as a normal user. It does not install a driver, service or firewall rule, invoke a shell, change Windows security policy, or read existing saved Wi-Fi passwords.

## Permissions and persistence

- Constructing `NativeWifi` has no side effects. Wi-Fi operations occur only when the UI calls the API after its permission gate.
- Scanning uses `WlanScan`, `WlanGetAvailableNetworkList` and `WlanGetNetworkBssList`. Windows 11 may require location consent for these APIs, even though EveryWifi uses Wi-Fi information for connecting. Access-denied errors explain the location settings and company-policy possibility. [Microsoft: Wi-Fi access and location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes)
- Connection creates a **per-user** Windows profile (`WLAN_PROFILE_USER = 2`) with a deterministic `EveryWifi-` name based on SSID bytes, security and BSSID. Different shops using the same SSID retain separate app profiles. The profile uses `manual` connection mode and `autoSwitch=false`. The password is passed to Windows as profile XML in process memory; the app writes no plaintext XML file and does not log the password. Windows stores the profile. The UI must disclose this before connection and offer removal. [Microsoft: WlanSetProfile](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetprofile)
- `DeleteProfile` accepts only names beginning with `EveryWifi-`. `DeleteAllAppProfiles` enumerates all wireless adapters and removes only per-user profiles with that prefix. Neither method reads their passwords or deletes unrelated saved profiles. Disconnect and delete require explicit user actions in the UI.
- No app-level automatic connection, automatic password retries, captive-portal login automation, or administrator elevation is implemented.

## Connection and security

The scanner retains the SSID's raw bytes, obtains Windows' authentication/cipher values, and merges saved-profile duplicates per adapter/security pair. It lists infrastructure networks only. `BuildProfileXml` uses the exact SSID hex bytes; display strings cannot change the target. It supports open/unencrypted, WPA2-Personal AES, and WPA3-Personal SAE AES. Enterprise, WEP, TKIP, OWE, unknown suites and inconsistent metadata are refused with a link-to-Windows-settings message in the UI, rather than guessed or downgraded.

WPA2 accepts 8–63 printable ASCII password characters or a 64-digit hexadecimal key. WPA3 currently accepts 8–63 printable ASCII characters and does not fall back to WPA2. Its availability still depends on the installed Windows version, Wi-Fi card and driver. [Microsoft WPA3 profile sample](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wpa3-personal-transition-profile-sample)

When a BSSID is available, `WlanConnect` receives a desired BSSID list. The app also queries the exact interface after requesting connection and verifies the raw SSID and BSSID. A successful native request alone is not reported as connected. A 20-second timeout is reported as unconfirmed; it does not disconnect another network. Windows may finish a pending request later, so the UI must continue to display fresh connection state. A BSSID is useful for avoiding accidental same-name selection; it is not cryptographic proof that a hotspot belongs to a business.

Operations are serialized and bounded; scan completion uses Windows ACM notifications, with a 6.5-second wait for slow/faulty drivers. Scan failure, timeout or unavailable completion notifications produce a visible error; cached results are not returned as a newly completed scan. With multiple adapters, only adapters that reported successful completion contribute to the result. The resulting list is Windows' snapshot after that successful scan. Network and location-policy failures remain visible. Querying connection state does not issue a scan. App disposal cancels pending waits and unregisters notifications before closing the WLAN handle.

## Internet and portal checks

The separately gated `InternetProbe.CheckAsync` sends an HTTP GET to `http://www.msftconnecttest.com/connecttest.txt`, using no cookies or credentials, no automatic redirects and a six-second bound. It limits response data to 256 bytes. A correct `Microsoft Connect Test` response is `online`; redirects or replaced content are `captive` candidates. Network absence is `offline`. DNS, timeout, blocked endpoint and unusual HTTP responses are `unknown` rather than proof that the Internet is unavailable. This checks the computer's active route; with simultaneous Ethernet, VPN and Wi-Fi it does not prove that the selected Wi-Fi interface itself has Internet access.

Redirect targets must pass `IsSafePortalUrl`: absolute HTTP(S), no embedded credentials, controls, backslashes, or loopback hosts. Private gateway URLs are permitted because real captive portals commonly use them. The method validates URL protocol syntax, not operator identity. Opening a URL or entering credentials still requires the user's explicit action; the app never opens or authenticates a portal automatically.

## Validation limits

`tests/BackendTests.cs` verifies exact SSID bytes, XML escaping, supported/unsupported security, password limits, manual profiles, portal URL validation and unmanaged structure sizes/offsets without invoking Wi-Fi APIs. Live connection and disconnection were deliberately not exercised on the developer's network. Hardware testing is required before a public stable release, including Windows 10/11, denied location consent, more than one adapter, WPA3-capable hardware, wrong passwords and actual public captive portals.
