"use strict";

(() => {
  const tabs = Array.from(document.querySelectorAll("[data-tab]"));
  const toastElement = document.getElementById("demo-toast");
  let toastTimer;
  let connectionTimer;

  function toast(message) {
    clearTimeout(toastTimer);
    toastElement.textContent = message;
    toastElement.classList.add("visible");
    toastTimer = setTimeout(() => toastElement.classList.remove("visible"), 4500);
  }

  function selectTab(tab, focus = false) {
    tabs.forEach((item) => {
      const selected = item === tab;
      item.setAttribute("aria-selected", String(selected));
      item.tabIndex = selected ? 0 : -1;
      document.getElementById(item.getAttribute("aria-controls")).hidden = !selected;
    });
    if (focus) tab.focus();
  }

  tabs.forEach((tab, index) => {
    tab.addEventListener("click", () => selectTab(tab));
    tab.addEventListener("keydown", (event) => {
      let next;
      if (event.key === "ArrowRight" || event.key === "ArrowDown") next = (index + 1) % tabs.length;
      if (event.key === "ArrowLeft" || event.key === "ArrowUp") next = (index - 1 + tabs.length) % tabs.length;
      if (event.key === "Home") next = 0;
      if (event.key === "End") next = tabs.length - 1;
      if (next !== undefined) {
        event.preventDefault();
        selectTab(tabs[next], true);
      }
    });
  });

  const networkButtons = Array.from(document.querySelectorAll("[data-network]"));
  networkButtons[0].classList.add("active");
  networkButtons.forEach((button) => {
    button.addEventListener("click", () => {
      clearTimeout(connectionTimer);
      const portal = button.dataset.portal === "true";
      const network = button.dataset.network;
      document.getElementById("demo-network-name").textContent = network;
      document.getElementById("demo-status-label").textContent = "連線中 · 示意";
      document.getElementById("demo-status-text").textContent = "正在示範連線狀態，沒有建立真實連線。";
      networkButtons.forEach((item) => {
        item.classList.remove("active");
        item.textContent = item === button ? "連線中" : "試試連線";
        item.disabled = true;
      });
      connectionTimer = setTimeout(() => {
        document.getElementById("demo-status-label").textContent = portal ? "需要登入 · 示意" : "已連線 · 示意";
        document.getElementById("demo-status-text").textContent = portal ? "可能需要開啟登入頁，並自行完成服務驗證。" : "可使用網際網路 · 訊號良好";
        networkButtons.forEach((item) => {
          item.disabled = false;
          if (item === button) {
            item.classList.add("active");
            item.textContent = portal ? "需要登入" : "已連線";
          }
        });
        toast(portal ? "示意：入口頁的條款與驗證由使用者自行完成。" : "示意：每次連線都能查看網路與網際網路狀態。");
      }, 750);
    });
  });

  document.getElementById("demo-scan").addEventListener("click", () => {
    toast("這是介面示意。Windows App 會在取得同意後，讀取附近 Wi-Fi。");
  });

  document.getElementById("demo-check").addEventListener("click", () => {
    toast("示意：HTTP 連線檢查需由你觸發；此網頁沒有發出檢查請求。");
  });

  document.querySelectorAll("[data-demo-switch]").forEach((button) => {
    button.addEventListener("click", () => {
      const enabled = button.getAttribute("aria-checked") !== "true";
      button.setAttribute("aria-checked", String(enabled));
      toast(`示意設定已${enabled ? "開啟" : "關閉"}，沒有改動你的 Windows 設定。`);
    });
  });

  const search = document.getElementById("demo-search");
  search.addEventListener("input", () => {
    const query = search.value.trim().toLocaleLowerCase("zh-TW");
    let visibleCount = 0;
    document.querySelectorAll("[data-search]").forEach((item) => {
      const match = item.dataset.search.toLocaleLowerCase("zh-TW").includes(query);
      item.hidden = !match;
      if (match) visibleCount++;
    });
    document.getElementById("search-empty").hidden = visibleCount !== 0;
  });

  const filters = Array.from(document.querySelectorAll("[data-filter]"));
  filters.forEach((button) => {
    button.addEventListener("click", () => {
      filters.forEach((item) => {
        item.classList.toggle("active", item === button);
        item.setAttribute("aria-pressed", String(item === button));
      });
      document.querySelectorAll("[data-source]").forEach((item) => {
        item.hidden = button.dataset.filter !== "all" && item.dataset.source !== button.dataset.filter;
      });
    });
  });

  document.addEventListener("keydown", (event) => {
    const editing = /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName);
    if (event.key === "/" && !editing && !document.getElementById("panel-catalog").hidden) {
      event.preventDefault();
      search.focus();
    }
    if (event.key === "Escape") toastElement.classList.remove("visible");
  });
})();
