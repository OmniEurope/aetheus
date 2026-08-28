// SPDX-License-Identifier: EUPL-1.2
// SPDX-FileCopyrightText: 2026 Aetheus contributors
//
// Radzen DataGrid accessibility sanitizer.
//
// Radzen renders role="grid" + aria-activedescendant="{id}-active-item" + tabindex on the OUTER
// .rz-data-grid element, and role="rowgroup" on the inner <thead>/<tbody>, while the rows themselves are
// NOT exposed as role="row". axe-core (WCAG 2.0/2.1 A+AA) flags this half-built grid as CRITICAL:
//   - aria-required-children : role="grid" must contain role="row"/"rowgroup" children.
//   - aria-valid-attr-value  : aria-activedescendant points to "{id}-active-item", an element that only
//     exists WHILE a cell has keyboard focus - so it dangles (invalid reference) at rest.
// None is fixable from Blazor markup: the attributes are set by Radzen's own JS, not our components.
//
// We strip the whole malformed ARIA grid role-set after each render and let the NATIVE
// <table>/<thead>/<tbody>/<tr>/<th>/<td> semantics stand: a plain, consistent HTML table is both
// valid to axe AND genuinely accessible to a screen reader. IMPORTANT: removing role="grid" alone is
// NOT enough - it orphans the inner <thead role="rowgroup">/<tbody role="rowgroup"> (aria-required-parent:
// a rowgroup needs a grid/table/treegrid ancestor), which is a NEW critical violation. So we must also
// strip the grid-family roles (row/rowgroup/columnheader/rowheader/gridcell/cell/treegrid) from the grid's
// descendants and remove Radzen 11's role="presentation" from the inner table; the native table,
// thead/tbody and th elements then regain their consistent implicit roles. We deliberately SKIP any
// grid that currently contains keyboard focus, so
// Radzen's live cell-navigation (role="grid" + a valid, focused active-descendant) is left intact for a
// user actually tabbing through it; only the idle grids axe scans are sanitized. If proper grid semantics
// are later required, the real fix is upstream (Radzen exposing a full, valid grid role tree).
(function () {
    "use strict";

    // Explicit ARIA grid-family roles Radzen sprinkles on the table subtree. Removing them lets the
    // native table element semantics take over, so nothing is left orphaned/inconsistent for axe.
    var GRID_DESCENDANT_ROLES = {
        row: 1, rowgroup: 1, columnheader: 1, rowheader: 1, gridcell: 1, cell: 1, treegrid: 1, grid: 1
    };

    function stripGridRoles(grid) {
        var presentationTables = grid.querySelectorAll("table[role='presentation']");
        for (var i = 0; i < presentationTables.length; i++) {
            presentationTables[i].removeAttribute("role");
        }

        var roled = grid.querySelectorAll("[role]");
        for (var j = 0; j < roled.length; j++) {
            var el = roled[j];
            if (GRID_DESCENDANT_ROLES[el.getAttribute("role")]) {
                el.removeAttribute("role");
            }
        }
        // Once the grid-family roles are gone, remove only state attributes that native table elements
        // do not support. Keep aria-sort on <th>: its implicit columnheader role supports the attribute
        // and exposes the active sort direction to assistive technologies.
        // The grid element itself is included, not just its descendants: with AllowVirtualization
        // Radzen puts aria-rowcount on the OUTER .rz-data-grid, exactly where stripGridRoles has just
        // removed role="grid". aria-rowcount is only valid on grid/table/treegrid, so leaving it there
        // is a CRITICAL aria-allowed-attr violation - the one that appeared the day every table was
        // switched to infinite scroll. querySelectorAll never returns the root, hence the explicit union.
        var sortAttrs = [grid].concat(Array.prototype.slice.call(grid.querySelectorAll(
            "[aria-colindex],[aria-rowindex],[aria-colcount],[aria-rowcount],[aria-selected]")));
        for (var k = 0; k < sortAttrs.length; k++) {
            var s = sortAttrs[k];
            s.removeAttribute("aria-colindex");
            s.removeAttribute("aria-rowindex");
            s.removeAttribute("aria-colcount");
            s.removeAttribute("aria-rowcount");
            s.removeAttribute("aria-selected");
        }
    }

    // On narrow screens the table body is deliberately horizontally scrollable so that no column,
    // link or badge gets hidden. It can also scroll vertically inside fixed-height dashboard tiles.
    // A scroll container must be reachable by keyboard (notably in Safari); otherwise keyboard-only
    // users cannot reveal the clipped columns. Radzen owns this inner element, so make it focusable
    // only while it actually overflows and undo the attribute when the layout no longer needs it.
    function makeScrollableBodiesKeyboardReachable(grid) {
        var bodies = grid.querySelectorAll(".rz-data-grid-data");
        for (var i = 0; i < bodies.length; i++) {
            var body = bodies[i];
            var overflows = body.scrollWidth > body.clientWidth || body.scrollHeight > body.clientHeight;
            if (overflows) {
                body.setAttribute("tabindex", "0");
                body.setAttribute("data-aetheus-scrollable-body", "true");
            } else if (body.getAttribute("data-aetheus-scrollable-body") === "true") {
                body.removeAttribute("tabindex");
                body.removeAttribute("data-aetheus-scrollable-body");
            }
        }
    }

    function sanitize() {
        var focused = document.activeElement;
        var grids = document.querySelectorAll(".rz-data-grid");
        for (var i = 0; i < grids.length; i++) {
            var g = grids[i];
            // Leave a grid the user is actively navigating with the keyboard untouched.
            if (focused && g.contains(focused)) {
                continue;
            }
            var ad = g.getAttribute("aria-activedescendant");
            if (ad && !document.getElementById(ad)) {
                g.removeAttribute("aria-activedescendant");
            }
            if (g.getAttribute("role") === "grid") {
                g.removeAttribute("role");
            }
            // Strip the orphaned inner grid-family roles (thead/tbody rowgroup, any columnheader/row/etc.)
            // so the native table semantics remain consistent (no aria-required-parent violation).
            stripGridRoles(g);
            makeScrollableBodiesKeyboardReachable(g);
        }
    }

    var observer = null;
    var scheduled = false;

    function observe() {
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ["role", "aria-activedescendant", "aria-sort"]
        });
    }

    function run() {
        scheduled = false;
        // Disconnect while we mutate so our own attribute removals do not re-trigger the observer.
        observer.disconnect();
        sanitize();
        observe();
    }

    function schedule() {
        if (scheduled) { return; }
        scheduled = true;
        requestAnimationFrame(run);
    }

    function start() {
        observer = new MutationObserver(schedule);
        sanitize();
        observe();
        window.addEventListener("resize", schedule, { passive: true });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start);
    } else {
        start();
    }
})();
