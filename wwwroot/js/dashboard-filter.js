/* The dashboard's toolbar filter (issue #18).
 *
 * This is plain JS rather than a Blazor `@oninput` because ADR 0003's
 * amendment names search-as-you-type as local UI state, which must not
 * round-trip. The rows are all already in the page; a keystroke only decides
 * which of them are shown, so nothing here talks to the server - no fetch, no
 * circuit message, no re-render.
 *
 * The contract with Dashboard.razor:
 *   [data-ks-filter="#id"]  the input, naming the element it filters inside
 *   [data-ks-text]          a row, carrying everything it matches against
 *   [data-hidden]           set per row; app.css section 8 does the hiding
 *   [data-none]             set on the scope when nothing matched, which is
 *                           what reveals its .hub-list-none message
 */
(function () {
    "use strict";

    function applyFilter(input) {
        var scope = document.querySelector(input.getAttribute("data-ks-filter"));
        if (!scope) return;

        var term = input.value.trim().toLowerCase();
        var shown = 0;

        scope.querySelectorAll("[data-ks-text]").forEach(function (row) {
            var matches = term === "" ||
                row.getAttribute("data-ks-text").toLowerCase().indexOf(term) !== -1;

            row.setAttribute("data-hidden", matches ? "false" : "true");
            if (matches) shown++;
        });

        scope.setAttribute("data-none", shown === 0 ? "true" : "false");
    }

    function wire() {
        document.querySelectorAll("[data-ks-filter]").forEach(function (input) {
            // Enhanced navigation can hand back an input this already ran on,
            // so the listener is attached once...
            if (!input.__ksFilterWired) {
                input.__ksFilterWired = true;
                input.addEventListener("input", function () { applyFilter(input); });
            }

            // ...but the filter is re-applied every time, because the other
            // half of enhanced navigation is a table that has changed under a
            // term still sitting in the box. Without this, typing "hash1" on
            // Devices and clicking Books leaves the term on screen over
            // unfiltered rows - or worse, leaves a row hidden and the
            // "nothing matches" message up beside rows that are visible.
            applyFilter(input);
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", wire);
    } else {
        wire();
    }

    // Switching section is a navigation now, and Blazor's enhanced navigation
    // patches the DOM instead of reloading - so without this the filter stays
    // wired to the table it first saw and goes dead on the second section.
    // Deferred because blazor.web.js has not loaded when this script runs.
    // Same hook, and the same reason, as theme.js.
    document.addEventListener("DOMContentLoaded", function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
            window.Blazor.addEventListener("enhancedload", wire);
        }
    });
})();
