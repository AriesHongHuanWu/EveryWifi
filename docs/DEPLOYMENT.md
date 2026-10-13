# GitHub 與 Cloudflare Pages

儲存庫：`AriesHongHuanWu/EveryWifi`（公開）。靜態來源：`site/dist`，無第三方前端依賴。

Cloudflare Pages 名稱：`everywifi`，已連接 GitHub `AriesHongHuanWu/EveryWifi`。Production branch 為 `main`、production deployments enabled、preview branches all、build command 空白、output directory 為 `site/dist`、root directory 為 `/`（儲存庫根目錄）。推送 `main` 時自動部署網站。下載連結指向公開 GitHub Release，無需將 Windows 可執行檔加入原始碼歷史。

直接上傳也可部署：

```powershell
npx wrangler@4 pages deploy site/dist --project-name everywifi --branch main
```

Wrangler 需先在本機完成帳號登入。本專案不儲存 Cloudflare/GitHub token、OAuth refresh token、發行憑證或私鑰。

GitHub Actions 在 Windows 上建置與執行不切換網路的測試；推送 `v*` tag 時產生公開的 preview release、ZIP 與 SHA-256。除非提供可信簽章，此 preview 狀態必須保持明示。
