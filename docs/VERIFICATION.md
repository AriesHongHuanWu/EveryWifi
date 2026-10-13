# v0.1.0 驗證範圍

日期：2026-10-13。此版本為 unsigned preview，並非已完成各地現場相容性認證的正式版。

## 已驗證

- Windows 本機 .NET SDK 8.0.425，net48 WinForms：Release build 零錯誤、零警告。
- GitHub Actions Windows runner 已成功建置與執行檢查。
- 78 項檢查：62 項 backend assertions + 16 項資料與本機儲存檢查。backend 另於 x86 建置檢查 native 結構尺寸與偏移。
- SSID 原始 bytes／UTF-8 長度、XML 轉義、WPA2/WPA3／開放網路 profile、手動連線模式、不降低加密、私有 profile 刪除防護、安全網址 protocol 與 userinfo／loopback 拒絕。
- 官方資料 schema、14,695 熱點／22 縣市、519 家上游門市、23 個選項、來源網址、手動項目無猜測 SSID 或密碼、JSON 匯入邊界、原子寫入、null JSON fallback、DPAPI current-user roundtrip。
- 原生程式 `--preview` 開啟、附近清單與熱點頁面視覺檢查。修正品牌標題截斷。圖像 `app-preview.png` 是實際 Windows 視窗，網路與訊號是明示的示意資料。
- Landing page HTTP 200、JavaScript 語法、桌面與 390px 手機版、無水平溢出、搜尋、source filter、鍵盤分頁與登入狀態演示。
- 獨立審閱修正：門市 binding 覆蓋已記住密碼、預設路由誤稱 Wi-Fi 可用、未記住／失敗 profile 可清除、作業期間權限變更、掃描失敗視為新資料、同名不同 BSSID 的 portal URL 殘留、null JSON 啟動崩潰。

## 尚未完成的實機驗證

測試沒有對開發者電腦執行 Wi-Fi 掃描／連線／斷線、建立 profile、啟用開機執行或修改定位權限。連線流程是 Windows WLAN API 實作，尚需使用者在已獲授權的現場確認：

- Windows 10／不同 Windows 11 版本與網卡驅動的實際掃描通知。
- WPA2／WPA3-Personal 密碼登入、同名不同 BSSID、訊號消失與錯誤密碼。
- 各商家或公共服務的 captive portal、條款、手機驗證或讀者認證。
- 各路易莎門市的現行 SSID 與密碼；官方電話資料並非保證 Wi-Fi 密碼。
- 安全軟體、SmartScreen、可信簽章與發行信譽。權限 UI 並不能保證防毒放行。

熱點快照描述官方列出的地點，不是即時 Wi-Fi 感測結果、可用性監測或逐點已驗證連線紀錄。網際網路檢查使用系統預設路由，可能經 Ethernet/VPN，不能獨立證明選定 Wi-Fi 的上網能力。
