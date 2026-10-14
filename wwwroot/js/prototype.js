/* THROWAWAY PROTOTYPE SCRIPT - delete with Components/Prototype/.
 *
 * Three jobs:
 *   1. Arrow-key cycling between variants (the floating switcher).
 *   2. A perceived-latency probe, so "the client doesn't feel interactive
 *      enough" (docs/ROADMAP.md, UI direction) becomes a number instead of a
 *      vibe. Times click -> first DOM mutation inside [data-pt-root]. A
 *      Blazor @onclick has to go browser -> SignalR -> server -> diff -> back,
 *      so it lands in the tens-of-ms range locally and much worse remotely;
 *      a client-side interaction never mutates the server-rendered tree at
 *      all and is reported as such.
 *   3. The client-side text filters variant C uses instead of round-tripping.
 */
(function () {
    "use strict";

    var PROBE_TIMEOUT_MS = 1200;
    var SLOW_MS = 60;

    /* ---------------------------------------------------------------- *
     * 1. Variant cycling                                               *
     * ---------------------------------------------------------------- */
    function cycleVariant(step) {
        var bar = document.querySelector("[data-pt-switcher]");
        if (!bar) return;

        var variants = (bar.getAttribute("data-pt-variants") || "").split(",").filter(Boolean);
        var current = bar.getAttribute("data-pt-current");
        if (variants.length === 0) return;

        var index = variants.indexOf(current);
        if (index === -1) index = 0;

        var next = variants[(index + step + variants.length) % variants.length];
        var url = new URL(window.location.href);
        url.searchParams.set(bar.getAttribute("data-pt-param") || "variant", next);
        window.location.assign(url.toString());
    }

    function isTypingTarget(el) {
        if (!el) return false;
        var tag = el.tagName;
        return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
    }

    document.addEventListener("keydown", function (e) {
        if (e.metaKey || e.ctrlKey || e.altKey) return;
        if (isTypingTarget(e.target)) return;
        if (e.key === "ArrowLeft") { e.preventDefault(); cycleVariant(-1); }
        if (e.key === "ArrowRight") { e.preventDefault(); cycleVariant(1); }
    });

    /* ---------------------------------------------------------------- *
     * 2. Perceived-latency probe                                       *
     * ---------------------------------------------------------------- */
    var pending = null;

    function report(text, slow) {
        var out = document.querySelector("[data-pt-latency-value]");
        if (!out) return;
        out.textContent = text;
        out.setAttribute("data-slow", slow ? "true" : "false");
    }

    function settle(ms) {
        if (!pending) return;
        var label = pending.label;
        pending = null;
        if (ms === null) {
            report("client-only", false);
        } else {
            report(Math.round(ms) + " ms" + (label ? " · " + label : ""), ms >= SLOW_MS);
        }
    }

    document.addEventListener("click", function (e) {
        var probe = e.target && e.target.closest ? e.target.closest("[data-pt-probe]") : null;
        if (!probe) return;

        pending = { at: performance.now(), label: probe.getAttribute("data-pt-probe") || "" };
        report("measuring...", false);

        window.setTimeout(function () {
            // No mutation inside the rendered tree means nothing went to the
            // server - the interaction resolved in CSS alone.
            if (pending && performance.now() - pending.at >= PROBE_TIMEOUT_MS) settle(null);
        }, PROBE_TIMEOUT_MS + 20);
    }, true);

    function watchRoot() {
        var root = document.querySelector("[data-pt-root]");
        if (!root || root.__ptWatched) return;
        root.__ptWatched = true;

        new MutationObserver(function () {
            if (pending) settle(performance.now() - pending.at);
        }).observe(root, { childList: true, subtree: true, attributes: true, characterData: true });
    }

    /* ---------------------------------------------------------------- *
     * 3. Client-side text filters (variant C)                          *
     * ---------------------------------------------------------------- */
    function applyFilter(input) {
        var scope = document.querySelector(input.getAttribute("data-pt-filter"));
        if (!scope) return;

        // A command verb ("new alice") is not a filter term - strip it so the
        // list does not empty out while a command is being typed.
        var raw = input.value.trim().toLowerCase();
        var verbs = ["new ", "reset ", "deactivate ", "activate ", "delete ", "promote "];
        for (var v = 0; v < verbs.length; v++) {
            if (raw.indexOf(verbs[v]) === 0) { raw = raw.slice(verbs[v].length).trim(); break; }
        }

        var items = scope.querySelectorAll("[data-pt-text]");
        var shown = 0;

        items.forEach(function (item) {
            var match = raw === "" || item.getAttribute("data-pt-text").toLowerCase().indexOf(raw) !== -1;
            item.setAttribute("data-hidden", match ? "false" : "true");
            if (match) shown++;
        });

        scope.setAttribute("data-none", shown === 0 ? "true" : "false");
    }

    function wireFilters() {
        document.querySelectorAll("[data-pt-filter]").forEach(function (input) {
            if (input.__ptWired) return;
            input.__ptWired = true;
            input.addEventListener("input", function () { applyFilter(input); });
        });
    }

    function init() {
        watchRoot();
        wireFilters();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    // Blazor's enhanced navigation patches the DOM instead of reloading, so
    // re-wire after each navigation (same hook theme.js uses).
    document.addEventListener("DOMContentLoaded", function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
            window.Blazor.addEventListener("enhancedload", init);
        }
    });
})();
