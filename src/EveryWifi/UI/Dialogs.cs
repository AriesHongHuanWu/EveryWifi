using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EveryWifi.Core;

namespace EveryWifi.UI
{
    internal static class Theme
    {
        internal static readonly Color Ink = Color.FromArgb(20, 49, 55);
        internal static readonly Color Teal = Color.FromArgb(0, 113, 102);
        internal static readonly Color Paper = Color.FromArgb(245, 248, 247);
        internal static readonly Color Muted = Color.FromArgb(99, 118, 122);
        internal static Button Button(string text, bool primary = false)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(12, 4, 12, 4), FlatStyle = FlatStyle.Flat, BackColor = primary ? Teal : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            b.FlatAppearance.BorderColor = primary ? Teal : Color.FromArgb(218, 227, 224);
            return b;
        }
        internal static Label Label(string text, int size = 10, bool bold = false)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Ink, Font = new Font("Microsoft JhengHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 8, 0, 6), MaximumSize = new Size(580, 0) };
        }
        internal static void OpenUrl(string url)
        {
            if (!NativeWifi.IsSafePortalUrl(url)) throw new ArgumentException("網址不符合 HTTP/HTTPS 規則。");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
    internal sealed class ConsentDialog : Form
    {
        public AppSettings Result { get; private set; }
        public ConsentDialog(AppSettings previous)
        {
            Text = "EveryWifi · 由你決定權限"; Size = new Size(610, 510); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = new Font("Microsoft JhengHei UI", 10); MaximizeBox = false; MinimizeBox = false;
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28), AutoScroll = true };
            p.Controls.Add(Theme.Label("先選擇 EveryWifi 可以做什麼", 19, true));
            p.Controls.Add(Theme.Label("免帳號、無遙測。所有權限都能在設定中隨時變更。\n只瀏覽內建名錄時，不需要掃描或定位權限。"));
            var scan = new CheckBox { Text = "允許讀取附近 Wi-Fi 與連線狀態", AutoSize = true, Checked = previous.AllowScan };
            var save = new CheckBox { Text = "允許在此電腦記住密碼與連線紀錄（DPAPI 加密密碼）", AutoSize = true, Checked = previous.AllowSave };
            var probe = new CheckBox { Text = "允許檢查網際網路是否可用", AutoSize = true, Checked = previous.AllowInternetCheck };
            p.Controls.Add(scan);
            p.Controls.Add(Theme.Label("Windows 11 可能另要求系統定位權限，才能讀取 SSID/BSSID。\n本程式不讀取 GPS 座標，也不自動更改 Windows 隱私設定。", 9));
            p.Controls.Add(save);
            p.Controls.Add(Theme.Label("Windows 也會保存你要求建立的 Wi-Fi 設定檔。\n已記住頁面可移除 EveryWifi 建立的設定檔。", 9));
            p.Controls.Add(probe);
            p.Controls.Add(Theme.Label("向 http://www.msftconnecttest.com/connecttest.txt 傳送短請求。\n此 HTTP 探測只用來辨識需登入的網路，不傳送密碼。", 9));
            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
            var confirm = Theme.Button("儲存我的選擇", true); var cancel = Theme.Button("稍後，只瀏覽名錄");
            confirm.Click += (s, e) => { Result = new AppSettings { ConsentCompleted = true, AllowScan = scan.Checked, AllowSave = save.Checked, AllowInternetCheck = probe.Checked, AutoConnect = previous.AutoConnect && scan.Checked && save.Checked, Notifications = previous.Notifications, StartMinimized = previous.StartMinimized }; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(confirm); buttons.Controls.Add(cancel); p.Controls.Add(buttons); Controls.Add(p); AcceptButton = confirm; CancelButton = cancel;
        }
    }
    internal sealed class ConnectDialog : Form
    {
        public string Password { get; private set; }
        public bool Remember { get; private set; }
        public ConnectDialog(WifiNetwork network, Provider provider, List<Store> stores, RememberedNetwork remembered, bool allowSave)
        {
            Text = "連線至 " + network.Ssid; Size = new Size(650, 600); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = new Font("Microsoft JhengHei UI", 10); MaximizeBox = false; MinimizeBox = false;
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(28) };
            p.Controls.Add(Theme.Label(network.Ssid, 20, true));
            p.Controls.Add(Theme.Label($"訊號 {network.Signal}% · {network.Authentication} · {network.Bssid}\n按連線後，Windows 可能切換目前使用的 Wi-Fi。", 9));
            var password = new TextBox { Width = 560, UseSystemPasswordChar = true, Text = remembered?.Password ?? "", Enabled = network.Secure };
            if (network.Ssid == "LouisaCoffee")
            {
                p.Controls.Add(Theme.Label("選擇你實際所在的路易莎門市", 11, true));
                var search = new TextBox { Width = 560 }; var storeList = new ComboBox { Width = 560, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "name" };
                var candidates = new ComboBox { Width = 560, DropDownStyle = ComboBoxStyle.DropDownList };
                var address = Theme.Label("輸入門市、地址或電話搜尋；不會使用定位推測門市。", 9);
                bool bindingStores = false;
                Action bindStores = () => { bindingStores = true; storeList.DataSource = stores.Where(t => ((t.name ?? "") + (t.address ?? "") + (t.phone ?? "")).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).Take(200).ToList(); storeList.SelectedIndex = -1; bindingStores = false; };
                search.TextChanged += (s, e) => bindStores();
                storeList.SelectedIndexChanged += (s, e) => { if (bindingStores) return; var store = storeList.SelectedItem as Store; if (store == null) return; address.Text = store.address + " · " + store.phone; candidates.DataSource = store.passwords ?? new string[0]; };
                candidates.SelectedIndexChanged += (s, e) => { if (candidates.SelectedItem != null) password.Text = (string)candidates.SelectedItem; };
                p.Controls.Add(search); p.Controls.Add(storeList); p.Controls.Add(address); p.Controls.Add(candidates);
                bindStores();
                p.Controls.Add(Theme.Label("候選密碼沿用開源專案的電話規則，並非官方保證。\n只使用你選擇的這一組，不會大量或依序嘗試其他門市。", 9));
            }
            else if (provider != null)
            {
                p.Controls.Add(Theme.Label(provider.name + " · " + provider.note, 9));
                if ((provider.passwords ?? new string[0]).Length > 0)
                {
                    var publicPassword = new ComboBox { Width = 560, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = provider.passwords };
                    publicPassword.SelectedIndexChanged += (s, e) => password.Text = (string)publicPassword.SelectedItem;
                    password.Text = provider.passwords[0]; p.Controls.Add(publicPassword);
                }
            }
            p.Controls.Add(Theme.Label(network.Secure ? "Wi-Fi 密碼（可改成店家提供的密碼）" : "此 Wi-Fi 為開放網路；可能還需要網頁登入。", 10, true));
            p.Controls.Add(password);
            if (network.Secure)
            {
                var show = new CheckBox { AutoSize = true, Text = "顯示密碼" }; show.CheckedChanged += (s, e) => password.UseSystemPasswordChar = !show.Checked; p.Controls.Add(show);
            }
            var remember = new CheckBox { AutoSize = true, Text = "在此電腦記住這台路由器（需允許儲存）", Enabled = allowSave && !string.IsNullOrWhiteSpace(network.Bssid) }; p.Controls.Add(remember);
            var authorized = new CheckBox { AutoSize = true, Text = "我已確認這是店家／單位公開提供，且我有權使用的網路" }; p.Controls.Add(authorized);
            p.Controls.Add(Theme.Label("相同 SSID 可能被冒用，請確認現場網路名稱。\nEveryWifi 不會自動同意條款、輸入手機號碼或略過付費登入。", 9));
            var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) }; var connect = Theme.Button("確認並連線", true); connect.Enabled = false; var cancel = Theme.Button("取消");
            authorized.CheckedChanged += (s, e) => connect.Enabled = authorized.Checked;
            connect.Click += (s, e) => { Password = network.Secure ? password.Text : ""; Remember = remember.Checked; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => Close(); actions.Controls.Add(connect); actions.Controls.Add(cancel); p.Controls.Add(actions); Controls.Add(p); AcceptButton = connect; CancelButton = cancel;
        }
    }
}
