using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using EveryWifi.Core;

namespace EveryWifi.UI
{
    internal sealed class MainForm : Form
    {
        private const string RepositoryUrl = "https://github.com/AriesHongHuanWu/EveryWifi";
        private AppSettings settings;
        private readonly bool preview;
        private readonly bool startTray;
        private NativeWifi wifi;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly TabControl pages;
        private readonly Label statusTitle, statusDetail, footer, title;
        private readonly Button scanButton, connectButton, probeButton, portalButton;
        private readonly DataGridView nearbyGrid, hotspotsGrid, savedGrid, historyGrid;
        private readonly ListBox providerList;
        private readonly Label providerDetail, catalogCount, hotspotCount;
        private readonly ComboBox cityFilter, categoryFilter;
        private readonly TextBox hotspotSearch, providerSearch;
        private readonly CheckBox autoConnect, notifications, startWithWindows, startHidden;
        private readonly Label permissionsLabel;
        private List<WifiNetwork> networks = new List<WifiNetwork>();
        private List<Provider> providers;
        private List<Store> stores;
        private List<Hotspot> hotspots;
        private List<RememberedNetwork> remembered;
        private List<HistoryEntry> history;
        private WifiState state = new WifiState();
        private string portalUrl;
        private bool busy, closing, refreshingSettings;
        private DateTime nextAutoAttempt = DateTime.MinValue;
        private DateTime nextScan = DateTime.MinValue;
        private string lastConnection = "";

        public MainForm(bool trayMode, bool previewMode)
        {
            startTray = trayMode; preview = previewMode;
            settings = preview ? new AppSettings { ConsentCompleted = true } : LocalStore.LoadSettings();
            providers = LocalStore.LoadProviders();
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            stores = LocalStore.ReadJson(Path.Combine(data, "louisa-stores.json"), new List<Store>());
            hotspots = LocalStore.ReadJson(Path.Combine(data, "hotspots.json"), new List<Hotspot>());
            remembered = !preview && settings.AllowSave ? LocalStore.LoadRemembered() : new List<RememberedNetwork>();
            history = !preview && settings.AllowSave ? LocalStore.ReadJson(Path.Combine(LocalStore.DirectoryPath, "history.json"), new List<HistoryEntry>()) : new List<HistoryEntry>();
            Text = "EveryWifi"; Size = new Size(1060, 760); MinimumSize = new Size(920, 650); StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Paper; ForeColor = Theme.Ink; Font = new Font("Microsoft JhengHei UI", 10); Icon = LoadIcon();
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ink, Margin = Padding.Empty };
            var brand = new Label { Text = "◉  EveryWifi", Location = new Point(18, 30), AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = Color.White }; sidebar.Controls.Add(brand);
            sidebar.Controls.Add(new Label { Text = "台灣公共 Wi-Fi 助手", Location = new Point(22, 77), AutoSize = true, ForeColor = Color.FromArgb(151, 194, 187), Font = new Font("Microsoft JhengHei UI", 9) });
            string[] nav = { "附近網路", "台灣熱點名錄", "商家與網路選項", "已記住與紀錄", "權限與設定" };
            pages = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(1, 1), SizeMode = TabSizeMode.Fixed, Multiline = true, Margin = new Padding(0) };
            for (int i = 0; i < nav.Length; i++)
            {
                int index = i; var b = Theme.Button(nav[i]); b.AutoSize = false; b.Size = new Size(161, 44); b.Location = new Point(12, 124 + i * 54); b.BackColor = Theme.Ink; b.ForeColor = Color.White; b.FlatAppearance.BorderSize = 0; b.TextAlign = ContentAlignment.MiddleLeft;
                b.Click += (s, e) => { pages.SelectedIndex = index; title.Text = nav[index]; }; sidebar.Controls.Add(b);
                pages.TabPages.Add(new TabPage(nav[i]) { BackColor = Theme.Paper, Padding = new Padding(6) });
            }
            var bottom = new Label { Text = "WINDOWS 原生程式\n免帳號 · 無遙測\n\nv0.1.0 preview", Dock = DockStyle.Bottom, Height = 98, Padding = new Padding(22, 0, 0, 20), ForeColor = Color.FromArgb(151, 194, 187), Font = new Font("Microsoft JhengHei UI", 9) }; sidebar.Controls.Add(bottom);
            shell.Controls.Add(sidebar, 0, 0);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 12, 22, 12), Margin = Padding.Empty };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 112)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            title = Theme.Label("附近網路", 20, true); title.Dock = DockStyle.Fill; header.Controls.Add(title, 0, 0); scanButton = Theme.Button("重新掃描", true); scanButton.Anchor = AnchorStyles.Right; scanButton.Click += async (s, e) => await RefreshAsync(true); header.Controls.Add(scanButton, 1, 0); content.Controls.Add(header, 0, 0);
            var status = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 0, 15), Padding = new Padding(20) };
            statusTitle = Theme.Label(preview ? "介面預覽 · 所有示例均為示意" : "先決定權限，再開始連線", 14, true); statusTitle.Location = new Point(20, 13); statusTitle.MaximumSize = new Size(720, 0);
            statusDetail = Theme.Label("只瀏覽名錄不需權限。右下角圖示可以隨時叫回視窗。", 9); statusDetail.Location = new Point(20, 47); statusDetail.MaximumSize = new Size(730, 0); status.Controls.Add(statusTitle); status.Controls.Add(statusDetail); content.Controls.Add(status, 0, 1);
            content.Controls.Add(pages, 0, 2); footer = Theme.Label("準備就緒 · 所有操作由你決定", 8); footer.Dock = DockStyle.Fill; content.Controls.Add(footer, 0, 3); shell.Controls.Add(content, 1, 0); Controls.Add(shell);

            var near = VerticalPage(pages.TabPages[0]);
            near.Controls.Add(Theme.Label(preview ? "示意清單 · 預覽模式不連線" : "實際掃描結果 · 選擇一個網路再確認連線", 10, true), 0, 0);
            nearbyGrid = Grid(new[] { "網路名稱", "訊號", "保護", "對應選項", "狀態" }); near.Controls.Add(nearbyGrid, 0, 1);
            var nearActions = ActionRow(); connectButton = Theme.Button("連線至選取網路", true); connectButton.Click += async (s, e) => await ConnectSelectedAsync();
            var disconnect = Theme.Button("中斷目前 Wi-Fi"); disconnect.Click += async (s, e) => await DisconnectAsync();
            probeButton = Theme.Button("檢查上網狀態"); probeButton.Click += async (s, e) => await ProbeAsync();
            portalButton = Theme.Button("開啟登入頁"); portalButton.Enabled = false; portalButton.Click += (s, e) => { if (portalUrl != null) Theme.OpenUrl(portalUrl); };
            nearActions.Controls.AddRange(new Control[] { connectButton, disconnect, probeButton, portalButton }); near.Controls.Add(nearActions, 0, 2);
            near.Controls.Add(Theme.Label("Windows 11 若要求定位權限，請自行在系統設定允許桌面應用程式存取。SSID 相同並不能保證是同一家店。", 9), 0, 3);
            nearbyGrid.CellDoubleClick += async (s, e) => { if (e.RowIndex >= 0) await ConnectSelectedAsync(); };

            var hotspotPage = VerticalPage(pages.TabPages[1]); var hf = ActionRow();
            cityFilter = new ComboBox { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList }; cityFilter.Items.Add("全部縣市"); cityFilter.Items.AddRange(hotspots.Select(h => h.city).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).Cast<object>().ToArray()); cityFilter.SelectedIndex = 0;
            hotspotSearch = new TextBox { Width = 285 }; hf.Controls.Add(cityFilter); hf.Controls.Add(Theme.Label("搜尋", 9)); hf.Controls.Add(hotspotSearch); hotspotPage.Controls.Add(hf, 0, 0);
            hotspotsGrid = Grid(new[] { "熱點名稱", "縣市", "地址" }); hotspotPage.Controls.Add(hotspotsGrid, 0, 1);
            var ha = ActionRow(); var official = Theme.Button("開啟官方 iTaiwan 地圖"); official.Click += (s, e) => Theme.OpenUrl("https://itaiwan.gov.tw/"); var showNetwork = Theme.Button("查看附近 iTaiwan"); showNetwork.Click += async (s, e) => { pages.SelectedIndex = 0; title.Text = nav[0]; await RefreshAsync(true); SelectSsid("iTaiwan"); }; ha.Controls.Add(official); ha.Controls.Add(showNetwork); hotspotPage.Controls.Add(ha, 0, 2);
            hotspotCount = Theme.Label("", 9); hotspotPage.Controls.Add(hotspotCount, 0, 3); cityFilter.SelectedIndexChanged += (s, e) => FilterHotspots(); hotspotSearch.TextChanged += (s, e) => FilterHotspots(); FilterHotspots();

            var catalogPage = VerticalPage(pages.TabPages[2]); var pf = ActionRow(); providerSearch = new TextBox { Width = 240 }; categoryFilter = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList }; categoryFilter.Items.Add("全部類型"); categoryFilter.Items.AddRange(providers.Select(t => t.category).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Cast<object>().ToArray()); categoryFilter.SelectedIndex = 0; pf.Controls.Add(Theme.Label("搜尋商家", 9)); pf.Controls.Add(providerSearch); pf.Controls.Add(categoryFilter); catalogPage.Controls.Add(pf, 0, 0);
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            providerList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, DisplayMember = "name", ItemHeight = 30, IntegralHeight = false, Font = new Font("Microsoft JhengHei UI", 11) };
            var detailPane = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; providerDetail = Theme.Label("選擇一個選項，查看來源與連線方式。", 10); providerDetail.MaximumSize = new Size(400, 0); detailPane.Controls.Add(providerDetail);
            var source = Theme.Button("查看資料來源"); source.Click += (s, e) => { var p = providerList.SelectedItem as Provider; if (p != null && !string.IsNullOrEmpty(p.sourceUrl)) Theme.OpenUrl(p.sourceUrl); }; detailPane.Controls.Add(source);
            var find = Theme.Button("在附近網路選取", true); find.Click += async (s, e) => { var p = providerList.SelectedItem as Provider; if (p == null) return; pages.SelectedIndex = 0; title.Text = nav[0]; await RefreshAsync(true); bool found = (p.ssids ?? new string[0]).Any(SelectSsid); if (!found) SetFooter("附近尚未掃描到此選項，請確認現場名稱。", false); }; detailPane.Controls.Add(find);
            split.Controls.Add(providerList, 0, 0); split.Controls.Add(detailPane, 1, 0); catalogPage.Controls.Add(split, 0, 1);
            var ca = ActionRow(); var import = Theme.Button("匯入公開網路 JSON"); import.Click += (s, e) => ImportProviders(); var template = Theme.Button("匯出範例格式"); template.Click += (s, e) => ExportTemplate(); ca.Controls.Add(import); ca.Controls.Add(template); catalogPage.Controls.Add(ca, 0, 2); catalogCount = Theme.Label("", 9); catalogPage.Controls.Add(catalogCount, 0, 3);
            providerSearch.TextChanged += (s, e) => FilterProviders(); categoryFilter.SelectedIndexChanged += (s, e) => FilterProviders(); providerList.SelectedIndexChanged += (s, e) => ShowProvider(); FilterProviders();

            var savedPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 }; savedPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); savedPage.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); savedPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); savedPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); savedPage.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); savedPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            savedPage.Controls.Add(Theme.Label("成功連線後，才會記住你勾選的路由器。", 9), 0, 0); savedGrid = Grid(new[] { "網路", "路由器 BSSID", "記住時間" }); savedPage.Controls.Add(savedGrid, 0, 1); var sa = ActionRow(); var forget = Theme.Button("移除選取紀錄與設定檔"); forget.Click += (s, e) => ForgetSelected(); sa.Controls.Add(forget); savedPage.Controls.Add(sa, 0, 2);
            savedPage.Controls.Add(Theme.Label("本機連線紀錄（最多 100 筆，不記錄密碼）", 9), 0, 3); historyGrid = Grid(new[] { "時間", "網路", "結果" }); savedPage.Controls.Add(historyGrid, 0, 4); var clearHistory = Theme.Button("清除紀錄"); clearHistory.Click += (s, e) => { history.Clear(); if (!preview && settings.AllowSave) SaveHistory(); RefreshSaved(); }; savedPage.Controls.Add(clearHistory, 0, 5); pages.TabPages[3].Controls.Add(savedPage); RefreshSaved();

            var preferences = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            preferences.Controls.Add(Theme.Label("每一項權限都由你決定", 15, true)); permissionsLabel = Theme.Label("", 10); preferences.Controls.Add(permissionsLabel); var permissions = Theme.Button("變更權限", true); permissions.Click += async (s, e) => { ChooseConsent(); await RefreshAsync(false); }; preferences.Controls.Add(permissions);
            autoConnect = new CheckBox { Text = "自動連線：只使用曾成功記住的同一台路由器", AutoSize = true, Margin = new Padding(0, 20, 0, 8) }; autoConnect.CheckedChanged += (s, e) => { if (refreshingSettings) return; settings.AutoConnect = autoConnect.Checked && settings.AllowScan && settings.AllowSave; PersistSettings(); }; preferences.Controls.Add(autoConnect);
            preferences.Controls.Add(Theme.Label("預設關閉；已有 Wi-Fi 連線時不切換。每台路由器失敗後至少等 5 分鐘，且只用先前成功的一組密碼。BSSID 可被仿冒，仍請確認網路來源。", 9));
            notifications = new CheckBox { Text = "顯示連線結果通知", AutoSize = true }; notifications.CheckedChanged += (s, e) => { if (refreshingSettings) return; settings.Notifications = notifications.Checked; PersistSettings(); }; preferences.Controls.Add(notifications);
            startWithWindows = new CheckBox { Text = "登入 Windows 時在右下角執行", AutoSize = true }; startWithWindows.CheckedChanged += (s, e) => { if (refreshingSettings || preview) return; try { LocalStore.SetStartup(startWithWindows.Checked); } catch (Exception ex) { SetFooter(ex.Message, true); } }; preferences.Controls.Add(startWithWindows);
            startHidden = new CheckBox { Text = "下次開啟時先收至通知區域", AutoSize = true }; startHidden.CheckedChanged += (s, e) => { if (refreshingSettings) return; settings.StartMinimized = startHidden.Checked; PersistSettings(); }; preferences.Controls.Add(startHidden);
            var pa = ActionRow(); var location = Theme.Button("Windows 定位設定"); location.Click += (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); var github = Theme.Button("原始碼與新版下載"); github.Click += (s, e) => Theme.OpenUrl(RepositoryUrl); pa.Controls.Add(location); pa.Controls.Add(github); preferences.Controls.Add(pa);
            preferences.Controls.Add(Theme.Label("資料位置：%LOCALAPPDATA%\\EveryWifi\n本機密碼以 Windows DPAPI 綁定目前帳號；不會上傳。Windows 設定檔也會由系統保存連線金鑰。\n未使用管理員權限、驅動程式、背景服務、排程工作或防毒排除。\n此預覽版尚未簽章，SmartScreen 可能提醒。請從正式儲存庫下載，核對 SHA-256；不建議關閉防毒。", 9));
            var dataButton = Theme.Button("開啟本機資料夾"); dataButton.Click += (s, e) => { if (preview) return; Directory.CreateDirectory(LocalStore.DirectoryPath); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LocalStore.DirectoryPath) { UseShellExecute = true }); }; preferences.Controls.Add(dataButton);
            var clearProfiles = Theme.Button("清除所有 EveryWifi Windows 設定檔");
            clearProfiles.Click += (s, e) => {
                if (preview || busy) return;
                if (MessageBox.Show(this, "移除所有 EveryWifi 建立的 Windows Wi-Fi 設定檔與本機記住的密碼？\n包含未勾選記住或失敗時產生的設定檔。可能需要重新輸入密碼。", "清除 EveryWifi 設定檔", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try { settings.AutoConnect = false; PersistSettings(); ApplySettings(); var result = Wifi.DeleteAllAppProfiles(); if (result.Success) { remembered.Clear(); string savedPath = Path.Combine(LocalStore.DirectoryPath, "saved-networks.dat"); if (File.Exists(savedPath)) File.Delete(savedPath); RefreshSaved(); } SetFooter(result.Message, !result.Success); } catch (Exception ex) { SetFooter(ex.Message, true); }
            };
            preferences.Controls.Add(clearProfiles); pages.TabPages[4].Controls.Add(preferences);

            tray = new NotifyIcon { Icon = Icon, Visible = true, Text = "EveryWifi · 就緒" }; var menu = new ContextMenuStrip(); menu.Items.Add("開啟 EveryWifi", null, (s, e) => RestoreWindow()); menu.Items.Add("重新掃描", null, async (s, e) => await RefreshAsync(true)); menu.Items.Add("檢查上網狀態", null, async (s, e) => await ProbeAsync()); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("權限與設定", null, (s, e) => { RestoreWindow(); pages.SelectedIndex = 4; title.Text = nav[4]; }); menu.Items.Add("結束 EveryWifi", null, (s, e) => { closing = true; Close(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += (s, e) => RestoreWindow();
            timer = new System.Windows.Forms.Timer { Interval = 30000 }; timer.Tick += async (s, e) => await RefreshAsync(false);
            FormClosing += (s, e) => { if (!closing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); if (!preview && settings.Notifications) tray.ShowBalloonTip(1800, "EveryWifi", "已收至右下角通知區域。右鍵圖示可結束程式。", ToolTipIcon.Info); } else { timer.Stop(); tray.Visible = false; tray.Dispose(); wifi?.Dispose(); } };
            Shown += async (s, e) =>
            {
                if (preview) { PopulatePreview(); return; }
                if (!settings.ConsentCompleted) ChooseConsent();
                ApplySettings();
                if (startTray || settings.StartMinimized) Hide();
                timer.Start(); await RefreshAsync(false);
            };
            ApplySettings();
        }

        private static Icon LoadIcon()
        {
            try { return new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "everywifi.ico")); } catch { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        }
        private static TableLayoutPanel VerticalPage(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 }; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53)); page.Controls.Add(layout); return layout;
        }
        private static FlowLayoutPanel ActionRow() => new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 7, 0, 0) };
        private static DataGridView Grid(string[] names)
        {
            var grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(235, 240, 238) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(231, 240, 236), ForeColor = Theme.Ink, Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold), Padding = new Padding(6) }; grid.ColumnHeadersHeight = 40;
            grid.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Ink, BackColor = Color.White, SelectionBackColor = Color.FromArgb(212, 239, 230), SelectionForeColor = Theme.Ink, Padding = new Padding(6), Font = new Font("Microsoft JhengHei UI", 9) }; grid.RowTemplate.Height = 42;
            foreach (string n in names) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = n, Name = n }); return grid;
        }
        private void SetFooter(string message, bool error) { footer.Text = message; footer.ForeColor = error ? Color.FromArgb(170, 70, 47) : Theme.Muted; }
        private void PersistSettings() { if (!preview) LocalStore.SaveSettings(settings); }
        private void ApplySettings()
        {
            refreshingSettings = true; autoConnect.Checked = settings.AutoConnect; autoConnect.Enabled = settings.AllowScan && settings.AllowSave && !preview; notifications.Checked = settings.Notifications; startHidden.Checked = settings.StartMinimized; startWithWindows.Checked = !preview && LocalStore.StartupEnabled(); refreshingSettings = false;
            permissionsLabel.Text = $"掃描與狀態：{(settings.AllowScan ? "已允許" : "未允許")}　｜　記住密碼：{(settings.AllowSave ? "已允許" : "未允許")}\n上網檢查：{(settings.AllowInternetCheck ? "已允許" : "未允許")}　｜　管理員權限：不需要";
            connectButton.Enabled = settings.AllowScan && !preview; probeButton.Enabled = settings.AllowScan && settings.AllowInternetCheck && !preview; scanButton.Enabled = !preview;
        }
        private void ChooseConsent()
        {
            if (preview || busy) return;
            using (var dialog = new ConsentDialog(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                settings = dialog.Result; PersistSettings(); remembered = settings.AllowSave ? LocalStore.LoadRemembered() : new List<RememberedNetwork>(); history = settings.AllowSave ? LocalStore.ReadJson(Path.Combine(LocalStore.DirectoryPath, "history.json"), new List<HistoryEntry>()) : new List<HistoryEntry>();
                if (!settings.AllowScan) { networks.Clear(); nearbyGrid.Rows.Clear(); state = new WifiState(); statusTitle.Text = "只瀏覽名錄 · 未允許 Wi-Fi 存取"; statusDetail.Text = "你可以在權限與設定中變更選擇。"; portalUrl = null; portalButton.Enabled = false; }
                RefreshSaved(); ApplySettings();
            }
        }
        private NativeWifi Wifi => wifi ?? (wifi = new NativeWifi());
        private async Task RefreshAsync(bool forceScan)
        {
            if (preview || busy) return;
            if (!settings.AllowScan) { if (forceScan) ChooseConsent(); if (!settings.AllowScan) { SetFooter("未允許讀取 Wi-Fi；仍可瀏覽全部名錄。", false); return; } }
            busy = true; scanButton.Enabled = false;
            try
            {
                state = await Wifi.GetStateAsync();
                if (!settings.AllowScan) return;
                statusTitle.Text = state.Connected ? "已連上 Wi-Fi · " + state.Ssid : "Wi-Fi 尚未連線";
                statusDetail.Text = state.Connected ? $"訊號 {state.Signal}% · {state.InterfaceName} · IP {state.IpAddress ?? "取得中"}\n接收／傳送鏈路 {state.RxMbps}/{state.TxMbps} Mbps · 尚未代表可上網" : "選擇附近的公開網路，確認後才會連線。";
                tray.Text = ("EveryWifi · " + (state.Connected ? state.Ssid : "未連線")).Substring(0, Math.Min(63, ("EveryWifi · " + (state.Connected ? state.Ssid : "未連線")).Length));
                string association = state.Connected ? state.InterfaceId + "|" + state.Ssid + "|" + state.Bssid : "";
                if (association != lastConnection) { lastConnection = association; portalUrl = null; portalButton.Enabled = false; }
                if (forceScan || DateTime.UtcNow >= nextScan)
                {
                    SetFooter("正在透過 Windows 掃描附近 Wi-Fi…", false); networks = await Wifi.ScanAsync(); nextScan = DateTime.UtcNow.AddSeconds(60); if (!settings.AllowScan) { networks.Clear(); return; } DrawNetworks();
                }
                SetFooter($"更新於 {DateTime.Now:HH:mm:ss} · {networks.Count} 個附近網路 · 名錄 {hotspots.Count:N0} 個熱點", false);
                if (!state.Connected && settings.AutoConnect && settings.AllowSave && DateTime.UtcNow >= nextAutoAttempt)
                {
                    var known = networks.OrderByDescending(n => n.Signal).FirstOrDefault(n => remembered.Any(r => SameRouter(r, n)));
                    if (known != null) { nextAutoAttempt = DateTime.UtcNow.AddMinutes(5); var saved = remembered.First(r => SameRouter(r, known)); await ConnectNetworkAsync(known, saved.Password, false, true); }
                }
            }
            catch (Exception ex) { statusTitle.Text = "Wi-Fi 存取尚未完成"; statusDetail.Text = ex.Message; SetFooter("請檢查 Wi-Fi 開關、系統定位權限與 WLAN AutoConfig 服務。", true); }
            finally { busy = false; scanButton.Enabled = true; }
        }
        private void DrawNetworks()
        {
            nearbyGrid.Rows.Clear();
            foreach (var n in networks.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal))
            {
                var p = ProviderFor(n.Ssid); int index = nearbyGrid.Rows.Add(n.Ssid, n.Signal + "%", n.Secure ? n.Authentication : "開放", p?.name ?? "手動連線", n.Connected ? "已連線" : "可選取"); nearbyGrid.Rows[index].Tag = n;
            }
        }
        private Provider ProviderFor(string ssid) => providers.FirstOrDefault(p => (p.ssids ?? new string[0]).Contains(ssid, StringComparer.Ordinal));
        private static bool SameRouter(RememberedNetwork r, WifiNetwork n) => !string.IsNullOrWhiteSpace(n.Bssid) && string.Equals(r.Bssid, n.Bssid, StringComparison.OrdinalIgnoreCase) && r.Ssid == n.Ssid && r.Authentication == n.Authentication && r.Cipher == n.Cipher;
        private async Task ConnectSelectedAsync()
        {
            if (busy || preview || !settings.AllowScan) return;
            var network = nearbyGrid.CurrentRow?.Tag as WifiNetwork; if (network == null) { SetFooter("請先選擇附近網路。", false); return; }
            var saved = remembered.FirstOrDefault(r => SameRouter(r, network));
            using (var dialog = new ConnectDialog(network, ProviderFor(network.Ssid), stores, saved, settings.AllowSave))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                busy = true; connectButton.Enabled = false;
                try { await ConnectNetworkAsync(network, dialog.Password, dialog.Remember, false); }
                catch (Exception ex) { SetFooter(ex.Message, true); }
                finally { busy = false; connectButton.Enabled = settings.AllowScan; }
            }
        }
        private async Task ConnectNetworkAsync(WifiNetwork network, string password, bool remember, bool automatic)
        {
            if (!settings.AllowScan || (automatic && (!settings.AutoConnect || !settings.AllowSave))) return;
            if (automatic) { var current = await Wifi.GetStateAsync(); if (current.Connected || !settings.AllowScan || !settings.AutoConnect || !settings.AllowSave) return; }
            portalUrl = null; portalButton.Enabled = false;
            statusTitle.Text = "正在連線 · " + network.Ssid; SetFooter("等待 Windows 回報實際連線結果…", false);
            var result = await Wifi.ConnectAsync(network, password);
            Log(network.Ssid, result.Message);
            statusTitle.Text = result.Success ? "已連上 Wi-Fi · " + network.Ssid : "連線未完成"; statusDetail.Text = result.Message;
            if (result.Success && remember && settings.AllowSave && !string.IsNullOrEmpty(network.Bssid))
            {
                remembered.RemoveAll(r => SameRouter(r, network)); remembered.Add(new RememberedNetwork { Ssid = network.Ssid, Bssid = network.Bssid, SsidBytes = network.SsidBytes, Authentication = network.Authentication, Cipher = network.Cipher, Password = password, InterfaceId = network.InterfaceId, ProfileName = result.ProfileName, SavedAt = DateTime.Now }); LocalStore.SaveRemembered(remembered); RefreshSaved();
            }
            SetFooter(result.Message, !result.Success);
            if (settings.Notifications) tray.ShowBalloonTip(2500, "EveryWifi", result.Success ? "已連上 " + network.Ssid + "；可按「檢查上網狀態」確認網際網路。" : result.Message, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
            state = await Wifi.GetStateAsync(); networks.ForEach(n => n.Connected = state.Connected && n.Ssid == state.Ssid && string.Equals(n.Bssid, state.Bssid, StringComparison.OrdinalIgnoreCase)); DrawNetworks();
        }
        private async Task DisconnectAsync()
        {
            if (preview || busy || !settings.AllowScan) return;
            if (MessageBox.Show(this, "將中斷目前的 Wi-Fi 連線，並關閉 EveryWifi 自動連線。", "確認中斷", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            busy = true;
            try { settings.AutoConnect = false; portalUrl = null; portalButton.Enabled = false; PersistSettings(); ApplySettings(); state = await Wifi.GetStateAsync(); if (state.Connected) { var r = await Wifi.DisconnectAsync(state.InterfaceId); SetFooter(r.Message, !r.Success); Log(state.Ssid, r.Message); } }
            catch (Exception ex) { SetFooter(ex.Message, true); }
            finally { busy = false; }
            await RefreshAsync(false);
        }
        private async Task ProbeAsync()
        {
            if (preview || busy) return;
            if (!settings.AllowScan || !settings.AllowInternetCheck) { ChooseConsent(); if (!settings.AllowScan || !settings.AllowInternetCheck) return; }
            busy = true;
            try { var result = await new InternetProbe().CheckAsync(); if (!settings.AllowInternetCheck) return; statusTitle.Text = result.Status == "online" ? "目前系統網路可上網" : result.Status == "captive" ? "目前系統網路可能需要網頁登入" : "尚未確認可上網"; statusDetail.Text = result.Detail + "\n此檢查使用 Windows 預設路由，可能經由 Ethernet／VPN，不代表指定 Wi-Fi 單獨可用。"; portalUrl = result.Status == "captive" ? (NativeWifi.IsSafePortalUrl(result.PortalUrl) ? result.PortalUrl : "http://www.msftconnecttest.com/redirect") : null; portalButton.Enabled = portalUrl != null; SetFooter("檢查完成於 " + DateTime.Now.ToString("HH:mm:ss"), false); }
            catch (Exception ex) { SetFooter(ex.Message, true); }
            finally { busy = false; }
        }
        private bool SelectSsid(string ssid)
        {
            foreach (DataGridViewRow row in nearbyGrid.Rows) if ((row.Tag as WifiNetwork)?.Ssid == ssid) { row.Selected = true; nearbyGrid.CurrentCell = row.Cells[0]; return true; } return false;
        }
        private void FilterHotspots()
        {
            if (hotspotsGrid == null) return;
            string search = hotspotSearch.Text.Trim(); string city = cityFilter.SelectedItem as string;
            var filtered = hotspots.Where(h => (city == "全部縣市" || h.city == city) && ((h.name ?? "") + (h.address ?? "")).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            hotspotsGrid.Rows.Clear(); foreach (var h in filtered.Take(300)) hotspotsGrid.Rows.Add(h.name, h.city, h.address);
            hotspotCount.Text = $"官方名錄 {hotspots.Count:N0} 處 · 符合 {filtered.Count:N0} 處 · 顯示前 300 筆\n名錄快照不代表現場有訊號或仍正常服務；請以現場掃描與官方網站為準。";
        }
        private void FilterProviders()
        {
            if (providerList == null) return;
            var list = providers.Where(p => (categoryFilter.SelectedIndex <= 0 || p.category == categoryFilter.SelectedItem as string) && ((p.name ?? "") + (p.note ?? "") + string.Join(" ", p.ssids ?? new string[0])).IndexOf(providerSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList(); providerList.DataSource = list;
            catalogCount.Text = $"{providers.Count} 個商家／網路選項 · 路易莎 {stores.Count:N0} 個門市\n需店家確認的選項會標為手動；只對實際掃描到的精確 SSID 建立連線。";
        }
        private static string AuthLabel(string auth) => auth == "portal" ? "連線後需網頁登入" : auth == "open" ? "開放網路" : auth == "store-password" ? "門市個別密碼" : "手動確認現場資訊";
        private void ShowProvider()
        {
            var p = providerList.SelectedItem as Provider; if (p == null) return;
            providerDetail.Text = $"{p.name}\n\n{AuthLabel(p.auth)}\nSSID：{(((p.ssids ?? new string[0]).Length == 0) ? "以現場公告為準" : string.Join("、", p.ssids))}\n\n{p.note}\n\n來源：{p.sourceLabel}\n查核日期：{p.verifiedOn}\n\n名錄提供選項，並不保證每家分店都有 Wi-Fi。";
        }
        private void ImportProviders()
        {
            if (preview) return;
            using (var dialog = new OpenFileDialog { Filter = "公開 Wi-Fi JSON|*.json", Title = "匯入你有權使用的公開網路資料" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { var list = LocalStore.ValidateImport(dialog.FileName); if (MessageBox.Show(this, $"匯入 {list.Count} 個選項到本機。公開密碼會以一般 JSON 保存；請勿匯入私人或機密憑證。這不會觸發連線。", "檢視匯入", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return; var existing = LocalStore.ReadJson(Path.Combine(LocalStore.DirectoryPath, "custom-providers.json"), new List<Provider>()); var merged = existing.Where(p => !list.Any(n => n.id == p.id)).Concat(list).ToList(); LocalStore.WriteJson(Path.Combine(LocalStore.DirectoryPath, "custom-providers.json"), merged); providers = LocalStore.LoadProviders(); refreshingSettings = true; categoryFilter.Items.Clear(); categoryFilter.Items.Add("全部類型"); categoryFilter.Items.AddRange(providers.Select(p => p.category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Cast<object>().ToArray()); categoryFilter.SelectedIndex = 0; refreshingSettings = false; FilterProviders(); SetFooter("已匯入 " + list.Count + " 個選項；尚未連線。", false); }
                catch (Exception ex) { SetFooter("匯入失敗：" + ex.Message, true); }
            }
        }
        private void ExportTemplate()
        {
            if (preview) return;
            using (var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "EveryWifi-public-networks.example.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) LocalStore.WriteJson(dialog.FileName, new[] { new Provider { id = "my-public-cafe", name = "我的公開網路", category = "自訂", auth = "manual", ssids = new[] { "請填現場公開SSID" }, passwords = new string[0], sourceUrl = "https://example.com/", sourceLabel = "現場公開公告", verifiedOn = DateTime.Now.ToString("yyyy-MM-dd"), note = "請確認場地主人允許使用，切勿加入私人帳號或密碼。" } });
        }
        private void RefreshSaved()
        {
            savedGrid.Rows.Clear(); foreach (var r in remembered) { int i = savedGrid.Rows.Add(r.Ssid, r.Bssid, r.SavedAt.ToString("yyyy-MM-dd HH:mm")); savedGrid.Rows[i].Tag = r; }
            historyGrid.Rows.Clear(); foreach (var h in history.OrderByDescending(h => h.Time)) historyGrid.Rows.Add(h.Time.ToString("yyyy-MM-dd HH:mm:ss"), h.Network, h.Result);
        }
        private void Log(string ssid, string result)
        {
            if (preview || !settings.AllowSave) return; history.Add(new HistoryEntry { Time = DateTime.Now, Network = ssid, Result = result }); history = history.OrderByDescending(h => h.Time).Take(100).ToList(); SaveHistory(); RefreshSaved();
        }
        private void SaveHistory() => LocalStore.WriteJson(Path.Combine(LocalStore.DirectoryPath, "history.json"), history);
        private void ForgetSelected()
        {
            if (preview || busy || !settings.AllowSave) return; var selected = savedGrid.CurrentRow?.Tag as RememberedNetwork; if (selected == null) return;
            if (MessageBox.Show(this, "刪除此路由器的本機密碼與 EveryWifi 建立的 Windows 設定檔？\n不會移除其他程式建立的設定檔。", "移除紀錄", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try { var result = Wifi.DeleteProfile(selected.InterfaceId, selected.ProfileName); if (!result.Success) { SetFooter(result.Message, true); return; } remembered.Remove(selected); LocalStore.SaveRemembered(remembered); RefreshSaved(); SetFooter("已移除本機密碼與設定檔。", false); } catch (Exception ex) { SetFooter(ex.Message, true); }
        }
        private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        private void PopulatePreview()
        {
            networks = new List<WifiNetwork> { new WifiNetwork { Ssid = "iTaiwan", Signal = 92, Authentication = "open", Cipher = "none" }, new WifiNetwork { Ssid = "LouisaCoffee", Signal = 78, Secure = true, Authentication = "WPA2PSK", Cipher = "AES" }, new WifiNetwork { Ssid = "TPE-Free", Signal = 63, Authentication = "open", Cipher = "none" } }; DrawNetworks(); statusTitle.Text = "介面預覽 · 網路與訊號均為示意"; statusDetail.Text = "預覽模式不讀取系統 Wi-Fi、不建立設定檔、不保存任何資料。"; SetFooter("示意模式 · 真實使用需先選擇權限", false);
        }
    }
}
