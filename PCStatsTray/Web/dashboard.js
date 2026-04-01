(function () {
  "use strict";

  var VISIBILITY_KEY = "pcstats-term-visible-v8";
  var COLLAPSE_KEY = "pcstats-collapsed-v8";
  var THEME_KEY = "pcstats-term-theme-v1";
  var TYPEWRITER_TEXT = "pc stats tray // lan dashboard";
  var MAX_HISTORY = 12;
  var SPARK_CHARS = [" ", "▂", "▃", "▄", "▅", "▆", "▇", "█"];
  var EMPTY_BAR = "<span class=\"bar-empty\">[N/A_VOL]</span>";

  var latestSnapshot = null;
  var refreshTimer = null;
  var typingTimer = null;
  var metricHistory = {};
  var visibility = loadJsonStorage(VISIBILITY_KEY);
  var collapsedGroups = loadJsonStorage(COLLAPSE_KEY);

  var els = {
    machinePill: document.getElementById("machine-pill"),
    updatedPill: document.getElementById("updated-pill"),
    visiblePill: document.getElementById("visible-pill"),
    statusText: document.getElementById("status-text"),
    errorBanner: document.getElementById("error-banner"),
    errorMessage: document.getElementById("error-message"),
    customizeWrapper: document.getElementById("customize-wrapper"),
    toggleGrid: document.getElementById("toggle-grid"),
    groupsRoot: document.getElementById("groups"),
    typingText: document.getElementById("typing-text")
  };

  init();

  function init() {
    applyStoredTheme();
    bindEvents();
    startTypewriter(TYPEWRITER_TEXT, els.typingText, 38);
    refresh();
  }

  function bindEvents() {
    var themeToggle = document.getElementById("theme-toggle");
    var toggleCustomize = document.getElementById("toggle-customize");
    var showAll = document.getElementById("show-all");
    var resetView = document.getElementById("reset-view");

    if (themeToggle) {
      themeToggle.addEventListener("click", function () {
        var nextDark = !document.documentElement.classList.contains("dark");
        document.documentElement.classList.toggle("dark", nextDark);
        persistStringStorage(THEME_KEY, nextDark ? "dark" : "light");
      });
    }

    if (toggleCustomize) {
      toggleCustomize.addEventListener("click", function () {
        if (els.customizeWrapper) {
          els.customizeWrapper.classList.toggle("open");
        }
      });
    }

    if (showAll) {
      showAll.addEventListener("click", function () {
        if (!latestSnapshot || !latestSnapshot.metrics) {
          return;
        }

        visibility = {};
        latestSnapshot.metrics.forEach(function (metric) {
          visibility[metric.key] = true;
        });
        persistVisibility();
        fullRender(latestSnapshot);
      });
    }

    if (resetView) {
      resetView.addEventListener("click", function () {
        visibility = {};
        persistVisibility();
        if (latestSnapshot) {
          fullRender(latestSnapshot);
        }
      });
    }

    window.addEventListener("error", function (event) {
      showError("FATAL EXCEPTION: " + (event && event.message ? event.message : "Unknown client error"));
    });
  }

  function applyStoredTheme() {
    var storedTheme = loadStringStorage(THEME_KEY);
    if (storedTheme === "light") {
      document.documentElement.classList.remove("dark");
      return;
    }

    document.documentElement.classList.add("dark");
    persistStringStorage(THEME_KEY, "dark");
  }

  function startTypewriter(text, target, speed) {
    if (!target) {
      return;
    }

    if (typingTimer) {
      clearTimeout(typingTimer);
    }

    target.textContent = "";

    function step(index) {
      if (index >= text.length) {
        return;
      }

      target.textContent += text.charAt(index);
      typingTimer = setTimeout(function () {
        step(index + 1);
      }, speed + Math.floor(Math.random() * 18));
    }

    typingTimer = setTimeout(function () {
      step(0);
    }, 500);
  }

  function refresh() {
    setStatus("connecting", "[ CONNECTING... ]");

    fetchWithTimeout("/api/metrics", 3500)
      .then(function (response) {
        if (!response.ok) {
          throw new Error("HTTP_" + response.status);
        }

        return response.json();
      })
      .then(function (snapshot) {
        handleSnapshot(snapshot);
      })
      .catch(function (error) {
        setStatus("offline", "[ OFFLINE - RETRYING ]");
        showError("FETCH_ERROR: " + normalizeErrorMessage(error));

        if (!latestSnapshot) {
          renderEmptyState(
            "CONNECTION_LOST",
            "&gt; The dashboard could not reach the local PC right now. Keep the app open and make sure this phone is on the same LAN."
          );
        }

        scheduleRefresh(3000);
      });
  }

  function handleSnapshot(snapshot) {
    var previousSnapshot = latestSnapshot;
    document.title = (snapshot.machineName || "PC Stats Tray") + " Terminal Dashboard";

    setStatus("online", "[ ACTIVE - STREAMING ]");
    clearError();
    updateHeader(snapshot);
    updateMetricHistory(snapshot.metrics || []);

    if (shouldFullRender(previousSnapshot, snapshot)) {
      fullRender(snapshot);
    } else {
      updateRender(snapshot);
    }

    latestSnapshot = snapshot;
    scheduleRefresh(snapshot.refreshIntervalMs || 2500);
  }

  function updateHeader(snapshot) {
    var safeName = snapshot.machineName ? String(snapshot.machineName).toLowerCase().replace(/\s+/g, "-") : "unknown-host";
    var updated = new Date(snapshot.generatedAtUtc);

    if (els.machinePill) {
      els.machinePill.textContent = safeName;
    }

    if (els.updatedPill) {
      els.updatedPill.textContent = formatClockTime(updated);
    }

    if (els.visiblePill) {
      els.visiblePill.textContent = buildVisibleCount(snapshot);
    }
  }

  function setStatus(mode, text) {
    if (!els.statusText) {
      return;
    }

    els.statusText.textContent = text;
    els.statusText.className = "status-text " + statusClassFor(mode);
  }

  function statusClassFor(mode) {
    if (mode === "online") {
      return "status-online";
    }

    if (mode === "offline") {
      return "status-offline";
    }

    return "status-connecting";
  }

  function scheduleRefresh(delay) {
    if (refreshTimer) {
      clearTimeout(refreshTimer);
    }

    refreshTimer = setTimeout(refresh, delay);
  }

  function shouldFullRender(previousSnapshot, snapshot) {
    if (!previousSnapshot) {
      return true;
    }

    var visibleKeys = (snapshot.metrics || [])
      .filter(isVisible)
      .map(function (metric) {
        return metric.key;
      })
      .sort()
      .join("|");

    var renderedKeys = Array.prototype.map.call(
      document.querySelectorAll("[data-metric-key]"),
      function (node) {
        return node.getAttribute("data-metric-key");
      }
    )
      .sort()
      .join("|");

    return visibleKeys !== renderedKeys;
  }

  function fullRender(snapshot) {
    renderToggleGrid(snapshot.metrics || []);

    var visibleMetrics = (snapshot.metrics || []).filter(isVisible);
    if (!visibleMetrics.length) {
      renderEmptyState(
        "OUTPUT_NULL",
        "&gt; No data streams selected. Run _config to enable the cards you want to see."
      );
      return;
    }

    var grouped = groupMetrics(visibleMetrics);
    els.groupsRoot.innerHTML = "";

    Object.keys(grouped).forEach(function (groupName, index) {
      els.groupsRoot.appendChild(createGroupSection(groupName, grouped[groupName], index));
    });
  }

  function updateRender(snapshot) {
    (snapshot.metrics || []).filter(isVisible).forEach(function (metric) {
      var analysis = analyzeValue(metric.value);
      var valueEl = document.getElementById("val-" + metric.key);
      var barEl = document.getElementById("bar-" + metric.key);
      var sparkEl = document.getElementById("spark-" + metric.key);
      var cardEl = document.getElementById("card-" + metric.key);

      if (!valueEl || !barEl || !sparkEl || !cardEl) {
        return;
      }

      valueEl.textContent = metric.available && metric.value ? metric.value : "ERR_NULL";
      valueEl.className = "metric-value " + analysis.valueClass;

      barEl.innerHTML = analysis.percentage !== null ? getAsciiBar(analysis.percentage) : EMPTY_BAR;
      barEl.className = "metric-bar " + analysis.valueClass;

      sparkEl.textContent = getSparkline(metric.key);
      sparkEl.className = "metric-spark " + analysis.sparkClass;

      cardEl.className = "group-card " + analysis.borderClass;
    });
  }

  function renderToggleGrid(metrics) {
    if (!els.toggleGrid) {
      return;
    }

    els.toggleGrid.innerHTML = "";

    metrics.forEach(function (metric) {
      var wrapper = document.createElement("label");
      wrapper.className = "toggle-card";

      var input = document.createElement("input");
      input.type = "checkbox";
      input.checked = isVisible(metric);
      input.addEventListener("change", function () {
        visibility[metric.key] = input.checked;
        persistVisibility();
        if (latestSnapshot) {
          fullRender(latestSnapshot);
        }
      });

      var surface = document.createElement("span");
      surface.className = "toggle-surface";

      var label = document.createElement("span");
      label.className = "toggle-label";
      label.textContent = codeLabel(metric.label);

      var state = document.createElement("span");
      state.className = "toggle-state";
      state.textContent = input.checked ? "[x]" : "[ ]";

      input.addEventListener("change", function () {
        state.textContent = input.checked ? "[x]" : "[ ]";
      });

      surface.appendChild(label);
      surface.appendChild(state);
      wrapper.appendChild(input);
      wrapper.appendChild(surface);

      els.toggleGrid.appendChild(wrapper);
    });
  }

  function renderEmptyState(title, copyHtml) {
    if (!els.groupsRoot) {
      return;
    }

    els.groupsRoot.innerHTML =
      "<section class=\"empty-state\">" +
      "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\">" +
      "<path d=\"M3 12h18\"></path>" +
      "<path d=\"M7 8l-4 4 4 4\"></path>" +
      "<path d=\"M17 8l4 4-4 4\"></path>" +
      "</svg>" +
      "<span class=\"empty-title\">" + escapeHtml(title) + "</span>" +
      "<span class=\"empty-copy\">" + copyHtml + "</span>" +
      "</section>";
  }

  function createGroupSection(groupName, metrics, groupIndex) {
    var section = document.createElement("section");
    section.className = "group-section";
    section.setAttribute("data-group-name", groupName);

    if (collapsedGroups[groupName]) {
      section.classList.add("is-collapsed");
    }

    var header = document.createElement("button");
    header.type = "button";
    header.className = "group-header";
    header.innerHTML =
      "<svg class=\"group-chevron\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">" +
      "<path d=\"M6 9l6 6 6-6\"></path>" +
      "</svg>" +
      "<span class=\"group-prefix\">::</span>" +
      "<span class=\"group-title\">[ " + escapeHtml(codeLabel(groupName)) + "_SYS ]</span>" +
      "<span class=\"group-rule\"></span>";

    header.addEventListener("click", function () {
      var isCollapsed = !section.classList.contains("is-collapsed");
      section.classList.toggle("is-collapsed", isCollapsed);
      collapsedGroups[groupName] = isCollapsed;
      persistCollapsedGroups();
    });

    var grid = document.createElement("div");
    grid.className = "group-grid";

    metrics.forEach(function (metric, metricIndex) {
      grid.appendChild(createMetricCard(metric, (groupIndex * 4) + metricIndex));
    });

    section.appendChild(header);
    section.appendChild(grid);
    return section;
  }

  function createMetricCard(metric, animationIndex) {
    var analysis = analyzeValue(metric.value);
    var card = document.createElement("article");
    card.id = "card-" + metric.key;
    card.className = "group-card card-enter " + analysis.borderClass;
    card.setAttribute("data-metric-key", metric.key);
    card.style.animationDelay = String(animationIndex * 0.04) + "s";

    card.innerHTML =
      "<div class=\"card-code\" title=\"" + escapeAttribute(metric.label) + "\">&gt; " + escapeHtml(codeLabel(metric.label)) + "</div>" +
      "<div class=\"card-value-shell\">" +
      "<div id=\"val-" + escapeAttribute(metric.key) + "\" class=\"metric-value " + analysis.valueClass + "\">" +
      escapeHtml(metric.available && metric.value ? metric.value : "ERR_NULL") +
      "</div>" +
      "<div class=\"metric-visual\">" +
      "<div id=\"bar-" + escapeAttribute(metric.key) + "\" class=\"metric-bar " + analysis.valueClass + "\">" +
      (analysis.percentage !== null ? getAsciiBar(analysis.percentage) : EMPTY_BAR) +
      "</div>" +
      "<div id=\"spark-" + escapeAttribute(metric.key) + "\" class=\"metric-spark " + analysis.sparkClass + "\" title=\"Recent history\">" +
      escapeHtml(getSparkline(metric.key)) +
      "</div>" +
      "</div>" +
      "</div>";

    return card;
  }

  function groupMetrics(metrics) {
    return metrics.reduce(function (result, metric) {
      var groupName = metric.group || "General";

      if (!result[groupName]) {
        result[groupName] = [];
      }

      result[groupName].push(metric);
      return result;
    }, {});
  }

  function updateMetricHistory(metrics) {
    metrics.forEach(function (metric) {
      var analysis = analyzeValue(metric.value);

      if (!metricHistory[metric.key]) {
        metricHistory[metric.key] = [];
      }

      metricHistory[metric.key].push(resolveHistoryValue(analysis));

      if (metricHistory[metric.key].length > MAX_HISTORY) {
        metricHistory[metric.key].shift();
      }
    });
  }

  function resolveHistoryValue(analysis) {
    if (analysis.percentage !== null) {
      return analysis.percentage;
    }

    if (analysis.numeric === null) {
      return 0;
    }

    return clamp(analysis.numeric, 0, 100);
  }

  function analyzeValue(value) {
    var result = {
      valueClass: "metric-neutral",
      borderClass: "metric-border-neutral",
      sparkClass: "metric-neutral",
      percentage: null,
      numeric: null
    };

    if (!value) {
      return result;
    }

    var raw = String(value);
    var normalized = raw.replace(/\u00c2/g, "");
    var match = /(-?\d+(\.\d+)?)/.exec(normalized);
    var numeric = match ? parseFloat(match[1]) : NaN;

    if (!isNaN(numeric)) {
      result.numeric = numeric;
    } else {
      return result;
    }

    if (isTemperature(normalized)) {
      if (numeric >= 85) {
        result.valueClass = "metric-hot";
        result.borderClass = "metric-border-hot";
        result.sparkClass = "metric-hot";
      } else if (numeric >= 70) {
        result.valueClass = "metric-warn";
        result.borderClass = "metric-border-warn";
        result.sparkClass = "metric-warn";
      } else {
        result.valueClass = "metric-good";
        result.borderClass = "metric-border-good";
        result.sparkClass = "metric-good";
      }

      result.percentage = clamp((numeric / 100) * 100, 0, 100);
      return result;
    }

    if (normalized.indexOf("%") !== -1) {
      result.percentage = clamp(numeric, 0, 100);

      if (numeric >= 90) {
        result.valueClass = "metric-hot";
        result.borderClass = "metric-border-hot";
        result.sparkClass = "metric-hot";
      } else if (numeric >= 75) {
        result.valueClass = "metric-warn";
        result.borderClass = "metric-border-warn";
        result.sparkClass = "metric-warn";
      } else {
        result.valueClass = "metric-good";
        result.borderClass = "metric-border-good";
        result.sparkClass = "metric-good";
      }

      return result;
    }

    if (/(mb|gb|mhz|ghz|w|ops\/s|days)/i.test(normalized)) {
      result.valueClass = "metric-cyan";
      result.borderClass = "metric-border-cyan";
      result.sparkClass = "metric-cyan";
      result.percentage = inferUnitPercentage(normalized, numeric);
      return result;
    }

    return result;
  }

  function inferUnitPercentage(value, numeric) {
    var lower = value.toLowerCase();

    if (lower.indexOf("mb/s") !== -1) {
      return clamp((numeric / 250) * 100, 0, 100);
    }

    if (lower.indexOf(" gb") !== -1 || lower.indexOf("gb ") !== -1) {
      return clamp((numeric / 32) * 100, 0, 100);
    }

    if (lower.indexOf(" w") !== -1 || lower.indexOf("w ") !== -1) {
      return clamp((numeric / 150) * 100, 0, 100);
    }

    if (lower.indexOf("ghz") !== -1) {
      return clamp((numeric / 6) * 100, 0, 100);
    }

    return clamp(numeric, 0, 100);
  }

  function isTemperature(value) {
    return value.indexOf("°C") !== -1 || value.indexOf("ºC") !== -1 || /(^|\s)c$/i.test(value) || /c$/i.test(value.trim());
  }

  function getAsciiBar(percentage) {
    var value = clamp(percentage, 0, 100);
    var filled = Math.round(value / 12.5);
    return "[" + "█".repeat(filled) + "<span class=\"bar-empty\">" + "░".repeat(8 - filled) + "</span>]";
  }

  function getSparkline(metricKey) {
    var history = metricHistory[metricKey] || [];
    var pieces = history.map(function (value) {
      var index = Math.max(0, Math.min(SPARK_CHARS.length - 1, Math.floor(clamp(value, 0, 100) / 12.5)));
      return SPARK_CHARS[index];
    }).join("");

    return pieces.padEnd(MAX_HISTORY, " ");
  }

  function isVisible(metric) {
    if (Object.prototype.hasOwnProperty.call(visibility, metric.key)) {
      return !!visibility[metric.key];
    }

    return !!metric.defaultVisible;
  }

  function codeLabel(value) {
    return String(value || "")
      .replace(/\s+/g, "_")
      .replace(/[^A-Za-z0-9_]/g, "_")
      .replace(/_+/g, "_")
      .replace(/^_+|_+$/g, "")
      .toUpperCase();
  }

  function persistVisibility() {
    persistJsonStorage(VISIBILITY_KEY, visibility);
  }

  function persistCollapsedGroups() {
    persistJsonStorage(COLLAPSE_KEY, collapsedGroups);
  }

  function showError(message) {
    if (!els.errorBanner || !els.errorMessage) {
      return;
    }

    els.errorBanner.hidden = false;
    els.errorMessage.textContent = message;
  }

  function clearError() {
    if (!els.errorBanner || !els.errorMessage) {
      return;
    }

    els.errorBanner.hidden = true;
    els.errorMessage.textContent = "";
  }

  function fetchWithTimeout(url, timeoutMs) {
    return new Promise(function (resolve, reject) {
      var timer = setTimeout(function () {
        reject(new Error("TIMEOUT"));
      }, timeoutMs);

      fetch(url, { cache: "no-store" })
        .then(function (response) {
          clearTimeout(timer);
          resolve(response);
        })
        .catch(function (error) {
          clearTimeout(timer);
          reject(error);
        });
    });
  }

  function formatClockTime(date) {
    if (!(date instanceof Date) || isNaN(date.getTime())) {
      return "--:--:--";
    }

    return [
      pad(date.getHours(), 2),
      pad(date.getMinutes(), 2),
      pad(date.getSeconds(), 2)
    ].join(":");
  }

  function buildVisibleCount(snapshot) {
    var metrics = snapshot && snapshot.metrics ? snapshot.metrics : [];
    var visibleCount = metrics.filter(isVisible).length;
    return String(visibleCount) + "/" + String(metrics.length);
  }

  function pad(value, width) {
    return String(value).padStart(width, "0");
  }

  function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
  }

  function normalizeErrorMessage(error) {
    if (!error) {
      return "Unknown error";
    }

    if (error.message) {
      return error.message;
    }

    return String(error);
  }

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function escapeAttribute(value) {
    return escapeHtml(value);
  }

  function loadJsonStorage(key) {
    try {
      var raw = localStorage.getItem(key);
      return raw ? JSON.parse(raw) : {};
    } catch (error) {
      return {};
    }
  }

  function persistJsonStorage(key, value) {
    try {
      localStorage.setItem(key, JSON.stringify(value));
    } catch (error) {
      // Ignore storage failures and keep the dashboard usable.
    }
  }

  function loadStringStorage(key) {
    try {
      return localStorage.getItem(key);
    } catch (error) {
      return null;
    }
  }

  function persistStringStorage(key, value) {
    try {
      localStorage.setItem(key, value);
    } catch (error) {
      // Ignore storage failures and keep the dashboard usable.
    }
  }
}());
