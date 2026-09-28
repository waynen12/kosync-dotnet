(function () {
    var STORAGE_KEY = "kosync-theme";

    function storedTheme() {
        return localStorage.getItem(STORAGE_KEY);
    }

    function systemPrefersDark() {
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    function resolveTheme() {
        return storedTheme() || (systemPrefersDark() ? "dark" : "light");
    }

    function updateToggleLabels(theme) {
        document.querySelectorAll("[data-theme-toggle]").forEach(function (el) {
            el.textContent = theme === "dark" ? "☀ Light" : "🌙 Dark";
        });
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        updateToggleLabels(theme);
    }

    function setTheme(theme) {
        localStorage.setItem(STORAGE_KEY, theme);
        applyTheme(theme);
    }

    function toggleTheme() {
        var current = document.documentElement.getAttribute("data-theme") || "light";
        setTheme(current === "dark" ? "light" : "dark");
    }

    window.kosyncTheme = { toggle: toggleTheme };

    function applyStoredTheme() {
        applyTheme(resolveTheme());
    }

    // Applied synchronously on first paint, and again after every Blazor
    // "enhanced navigation" - which patches the DOM in place rather than
    // doing a hard reload, and resets <html>'s attributes to match the
    // freshly rendered markup (server-rendered HTML never has data-theme,
    // only this script sets it). Without the second hook the theme would
    // silently reset to default on every in-app navigation, breaking the
    // "sticky per-user preference, independent of the route" contract
    // (issue #5). Blazor's own event bus (not a DOM event) is the only
    // place "enhancedload" fires, and blazor.web.js itself hasn't loaded
    // yet when this script runs (it's the last tag in <body>), so the
    // registration is deferred to DOMContentLoaded.
    applyStoredTheme();
    document.addEventListener("DOMContentLoaded", function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
            window.Blazor.addEventListener("enhancedload", applyStoredTheme);
        }
    });
})();
