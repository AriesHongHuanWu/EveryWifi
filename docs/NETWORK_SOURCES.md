# EveryWifi 網路資料來源

資料來源核對／下載日期：**2026-10-13（Asia/Taipei）**。這是文件與資料快照的核對日期，不代表在各實體場所完成連線測試。

## 收錄數量與可用性

- **14,695 筆官方公共熱點位置**，涵蓋全臺 22 個縣市。原始下載 14,710 筆，移除 15 筆「縣市＋名稱＋地址」完全相同的重複列。不同樓層／服務區名稱仍可能位於同一建物；本數量不是獨立基地台或同時可連線的網路數。
- **23 張網路服務卡片**：14 張已從官方來源核對 SSID 的入口服務、1 張 Louisa 上游來源的門市密碼服務、8 張人工確認的服務指引。人工卡片 `auth: manual`、`ssids: []` 不參與自動 SSID 辨認。
- **519 家 Louisa 門市資料**，共 861 個電話推導的候選密碼字串。它們是上游社群專案候選資料，**不是 861 個官方確認的 Wi-Fi 密碼**。沒有從公開門市電話推論為已驗證的 Wi-Fi 密碼。
- 一般公共 Wi-Fi 多數免 Wi-Fi 密碼，仍需使用者在供應者入口同意條款；圖書館 NLPI 另需讀者本人的帳密。EveryWifi 不代為接受業者條款，也不收集入口帳密。
- 掃描出同一個 SSID 不能證明 AP 所有者。使用前須核對現場標示或向場所人員確認。地點名錄沒有保證現場訊號、營業時段、設備正常運作或使用資格。

## 官方熱點開放資料

資料發布者：數位發展部。資料集：[iTaiwan 公共區域免費服務無線上網熱點](https://data.gov.tw/dataset/5962)。[官方 JSON 資源](https://itaiwan.gov.tw/ITaiwanDW/GetFile?fileName=IpSelect_tw.json&type=6)由該資料集直接連結。

授權：[政府資料開放授權條款 第 1 版](https://data.gov.tw/license)。EveryWifi 註明原始提供機關與來源；正規化及去除重複列由本專案完成。原始欄位 `Ministry / Area / Agency / Name / Address / Administration / Latitude / Longitude` 正規化為 `name / address / city / lat / lng / sourceUrl`，未增加推測密碼或每筆的 SSID。

`data/hotspots.meta.json` 記錄來源、擷取日期、原始與去重數量、22 縣市數及原始 SHA-256。這次下載原始 SHA-256：`a23ff282e7d724b1032a517112452ba5d7c71de8ac3b4be838425cbcda5bf602`。資料集的網頁詮釋資料日期與資源內容的即時更新日期可能不同，保留擷取日，不宣稱每筆於擷取日實地更新。

原始資料沒有 SSID、BSSID、加密種類、使用條款、密碼或 AP 即時狀態；**不得由名錄位置直接建立連線**。熱點瀏覽僅提供搜尋地點、開啟官方來源與位置。連線以 Windows 實際掃描到的網路及使用者選擇為準。座標按照官方資源保留；這次都在臺灣經緯度範圍內，但沒有逐筆重新測量或地理編碼。

重新整理快照（不會在背景自動執行）：

```powershell
powershell -File scripts/refresh-hotspots.ps1
```

可使用本地已下載的原始 JSON 重現正規化：

```powershell
powershell -File scripts/refresh-hotspots.ps1 -SourceFile path/to/IpSelect_tw.json
```

若官方格式改變或少於 1,000 筆，腳本報錯並保留原有資料；缺少或明顯超出臺灣範圍的座標會設為 0，供 UI 略過地圖定位。

## SSID 與服務卡片來源

`data/providers.json` 各列保存 `sourceUrl / sourceLabel / verifiedOn / note`。所有通用 `passwords` 都是空陣列；未找到官方公開統一密碼就不填入。

| 服務 | 已核對名稱／資格 | 官方來源 |
| --- | --- | --- |
| iTaiwan | `iTaiwan`；2020-07-01 起免註冊，仍需同意入口條款 | [iTaiwan 服務簡介](https://itaiwan.gov.tw/ITaiwanArticle/Contents?articleId=81)、[常見問題](https://itaiwan.gov.tw/ITaiwanArticle/Contents?articleId=80) |
| 臺北市 | `TPE-Free`；公共場所與指定捷運設施 | [臺北捷運無線網路](https://english.metro.taipei/cp.aspx?n=13151C00B88D0969)、[資訊局服務範圍](https://doit.gov.taipei/News_Content.aspx?n=6466282DBED7C6B2&s=E92A7CA626F7C021) |
| 新北市 | `NewTaipei`；官方現行文字無空格 | [資訊中心使用說明，更新日 2026-08-14](https://www.imc.ntpc.gov.tw/home.jsp?id=c5de478ccbdd4431) |
| 桃園市 | `iTaoyuan`；指定公共場所 | [文化局 2026-07-01 說明](https://culture.tycg.gov.tw/News_Content.aspx?n=11129&s=1470563) |
| 臺中市 | `iTaichung`；同意使用條款 | [官方使用說明](https://itaichung.taichung.gov.tw/ch/content/index-1.htm) |
| 臺南市 | `Tainan`；不使用舊名稱 `Tainan-WiFi` 代替當前 SSID | [官方 FAQ](https://wireless.tainan.gov.tw/qalist.html) |
| 台灣高鐵 | `THSR_freeWIFI_ALL`；車站及列車 | [車站服務](https://www.thsrc.com.tw/ArticleContent/5f2f04e5-7946-415a-bb46-41fff653bce0)、[列車設施](https://en.thsrc.com.tw/ArticleContent/b4e47490-61e4-4512-b65e-3ef772ec3621) |
| 桃園捷運 | 車站 `TyMetro`；英文現行頁列車廂 `Tymetro-WiFi1 / 2`，2022 中文公告另列 `TyMetro-train` | [旅客服務](https://www.tymetro.com.tw/tymetro-new/en/_pages/service/)、[2022 列車公告](https://www.tymetro.com.tw/tymetro-new/tw/_pages/news/show-1634-4.html)。官方頁面有差異，卡片明示須核對現場 |
| 高雄機場 | `FREE Kaohsiung-Airport`；每 30 分鐘重新確認入口 | [航空站官方機場服務](https://www.kia.gov.tw/FACNSER-service.html) |
| 星巴克 | `Starbucks WiFi`；接受條款 | [台灣星巴克官方教學](https://www.starbucks.com.tw/stores/wifi.jspx) |
| 南港展覽館 | `TWTC Free`；參觀人士免密碼，部分區域限 5 GHz | [2026 COMPUTEX 外貿協會手冊](https://cloudcdn.taiwantradeshows.com.tw/2026/computex/manual/exhibitor-manual-en.pdf)。展館內 Vendor／Press 的受限網路不收錄公開憑證 |
| 科教館 | `Ntsec_FreeWifi`；館舍開放區域免費服務 | [科教館官方服務資訊](https://www.ntsec.gov.tw/User/Query.aspx?q=wifi) |
| 國立公共資訊圖書館 | `NLPI`；讀者個人帳號與密碼；無借閱證可用館內 iTaiwan | [圖書館官方電腦版設定](https://www.nlpi.edu.tw/ReaderService/RelatedServices/ComputerAndInternet/WiFiSet/Diannao.htm) |
| 點一點免費上網 | `.1. Free Wi-Fi`；部分需閱覽廣告與條款 | [業者服務規章](https://www.d1dfreewifi.com.tw/)、[光華商場 2026-08-05 SSID 說明](https://www.gh3c.org.tw/1%E9%97%9C%E6%96%BC-2/3b-pms%E5%B0%88%E6%A5%AD%E7%B6%AD%E4%BF%AE%E6%9C%8D%E5%8B%99/) |

## 人工服務指引的限制

| 卡片 | 能證明的事項 | 未核實事項／處理方式 |
| --- | --- | --- |
| 麥當勞 | [台灣官方門市查詢](https://www.mcdonalds.com/tw/zh-tw/restaurant-locator.html)有無線網路篩選 | 台灣統一 SSID／密碼未查到；不用日本 SSID 代填 |
| IKEA 台灣 | [新店店](https://www.ikea.com.tw/zh/store/xin-dian/facility)與[台北城市店](https://www.ikea.com.tw/en/store/taipeicityshop-arena/facility)列免費 Wi-Fi | 未核實全台所有門市、統一 SSID 或通用密碼 |
| 怡客 | [官方 LINE 帳號](https://page.line.me/eyf5243j)列免費 Wi-Fi | 未公布可全店套用的 SSID／密碼；須向門市確認 |
| 7-ELEVEN | [2022 年官方青航門市新聞](https://7-11.com.tw/Company/esg/news_detail.aspx?id=855)列 ibon WiFi | 舊文件只支持所述地點／時期，不能當作 2026 全台門市 Wi-Fi 或登入方案保證 |
| 高雄捷運 | [2018 官方公告](https://www.krtc.com.tw/Information/news_more?id=5fd0a85a2602480eae0c392962049a95)曾提供 `.KRTC-Free WIFI / KRTC-Free WIFI` | 未找到更新的現行名稱確認，故不參與自動匹配 |
| 南科公共區域 | [官方 FAQ](https://www.stsp.gov.tw/web/WEB/Jsp/Page/cindex.jsp?PageNbr=3&frontTarget=DEFAULT&thisRootID=94)列 iTaiwan 及 Free-STSP WiFi 服務 | `Free-STSP WiFi` 未明確確認完整 SSID；須核對現場。封閉區域不因 Wi-Fi 存在而取得進入權 |
| 國家圖書館 | [官方說明](https://www.ncl.edu.tw/content_85.html)列 iTaiwan 與 `ncl_reader` | `ncl_reader` 有網站與資源限制；由使用者依目的手動選取 |
| 桃園機場 | [機場公司官方開放資料](https://data.gov.tw/en/datasets/6209)確認提供無線熱點 | 現行[機場 Wi-Fi 頁](https://www.taoyuan-airport.com/wifi)的動態文字未擷取到，未用第三方旅遊文章填入自動匹配名稱 |

全家、85 度 C、其他咖啡／餐飲／超商品牌未因網友討論而直接加入已確認的統一密碼。新增資料需有業者公開憑證、店家授權或本人現場取得；可由使用者自行記錄獲授權的網路。

## Louisa 上游與憑證來源

`data/louisa-stores.json` 原樣複製自 [madeyexz/louisa-wifi](https://github.com/madeyexz/louisa-wifi/) commit `8a41d60a5f9ca90698515aefb16699f4caef512a` 的 `data/stores.json`。上游 MIT License，Copyright (c) 2026 Ian Hsiao；完整授權另保存於 `docs/UPSTREAM_MIT_LICENSE.txt`。

上游 [README](https://github.com/madeyexz/louisa-wifi/blob/8a41d60a5f9ca90698515aefb16699f4caef512a/README.md)記述 `LouisaCoffee` 和電話推導慣例。路易莎[官方門市查詢](https://www.louisacoffee.co/visit)提供免費 Wi-Fi 的篩選與門市聯絡資料，但**公開電話號碼本身不是官方 Wi-Fi 密碼公布或全門市授權證據**。EveryWifi 將所有電話推導項目標為候選資料，要求選定所在門市，先核對現場服務，再由使用者確認選定單一密碼後才連線。

不允許以名錄逐店試密碼、掃描後批次套用候選密碼、繞過入口、驗證碼、付費頁或個人帳號登入。單次可操作的連線只針對使用者選擇且有使用權限的目前可見網路；連線結果來自 Windows 狀態與連通性檢查。
