/* The detail pages' row expander (issue #39).
 *
 * The one piece of BookDetail/DeviceDetail that needs script at all. Every
 * other disclosure on those pages is a native <details> - the metadata
 * editor, the merge block, both confirmations - but a <details> cannot wrap
 * two <tr>s, and a table row plus the drawer under it is exactly that. So
 * the expander is written here rather than as an @onclick: ADR 0003's
 * amendment names disclosure as local UI state, which must not round-trip,
 * and these pages are InteractiveServer - an @onclick would send a circuit
 * message and ship back a DOM diff to show markup already in the page.
 *
 * Delegated from the document rather than wired per button, which is what
 * makes it safe on an InteractiveServer page: Blazor owns these nodes and
 * re-renders them whenever a merge, a reset or a curate reloads the Book, so
 * a listener held on a button is a listener on whatever that button has
 * become. There is nothing to re-wire after a render batch or an enhanced
 * navigation, which is why - unlike dashboard-filter.js - this has no
 * `enhancedload` hook.
 *
 * The contract with BookDetail.razor and DeviceDetail.razor:
 *   [data-row-expand="<key>"]  the <button> in the Document cell, carrying
 *                              aria-expanded as the state of the pair
 *   [data-row-drawer="<key>"]  the <tr> under it, carrying `hidden`
 *
 * Keys are matched within the button's own <tbody>, so the two pages could
 * both number their rows from the same index without colliding - and a
 * second table on a page later would not either.
 */
(function () {
    "use strict";

    document.addEventListener("click", function (event) {
        var toggle = event.target.closest("[data-row-expand]");
        if (!toggle) return;

        var body = toggle.closest("tbody");
        if (!body) return;

        var key = toggle.getAttribute("data-row-expand");
        var drawer = body.querySelector('[data-row-drawer="' + key + '"]');
        if (!drawer) return;

        // aria-expanded is the state, not a mirror of it: the attribute a
        // screen reader reads and the attribute the row is hidden by are set
        // from the same decision, so they cannot drift apart.
        var expanded = toggle.getAttribute("aria-expanded") === "true";

        toggle.setAttribute("aria-expanded", expanded ? "false" : "true");
        drawer.hidden = expanded;
    });
})();
