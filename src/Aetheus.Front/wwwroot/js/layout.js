window.Aetheus = window.Aetheus || {};

Aetheus.goBack = function () {
    window.history.back();
};

Aetheus.hideSplash = function () {
    // Fade out and remove the single boot splash (#app-splash, declared in index.html).
    // Idempotent: a missing element (already removed) is a no-op.
    const el = document.getElementById('app-splash');
    if (!el) return;
    el.classList.add('app-splash--hidden');
    const remove = function () { if (el.parentNode) el.parentNode.removeChild(el); };
    el.addEventListener('transitionend', remove, { once: true });
    // Fallback if transitionend never fires (reduced-motion, display quirks, interrupted transition).
    setTimeout(remove, 600);
};

Aetheus.setTheme = function (cssUrl) {
    const link = document.querySelector('link[href*="material-"]');
    if (link) {
        link.href = cssUrl;
    }
};

Aetheus.setLang = function (lang) {
    document.documentElement.lang = lang.substring(0, 2);
};

// HBSY: phase-align the heartbeat ring's CSS fill loop with the real age of the last beat.
// Setting a negative animation-delay makes the loop start mid-cycle instead of from empty,
// so a freshly-loaded page shows the ring already part-filled to match the true elapsed time
// rather than resyncing only at the next beat. `secondsIntoCadence` is the real elapsed time
// modulo the cadence (computed component-side). A no-op when the element is gone.
Aetheus.syncHeartbeatPhase = function (el, secondsIntoCadence) {
    if (!el) return;
    el.style.animationDelay = '-' + secondsIntoCadence + 's';
};

Aetheus.lockTitle = function (title) {
    // Set the initial document title once. We intentionally do NOT install
    // an interval that overrides the title - Blazor's <PageTitle> updates
    // document.title per page, and a periodic re-write would clobber it
    // (e.g. showing "Aetheus" on the login screen instead of "Login").
    if (!document.title || document.title === 'Aetheus') {
        document.title = title;
    }
    if (window._aetheusTitleInterval) {
        clearInterval(window._aetheusTitleInterval);
        window._aetheusTitleInterval = null;
    }
};

// Prefixes every page title with a short tag (e.g. the dev-banner label "P4") so worktree
// tabs are distinguishable at a glance: "P4 <original title>". Blazor's <PageTitle> rewrites
// document.title on each navigation, so we observe the <title> element and re-apply the prefix
// after each change. No prefix is applied when the tag is empty (e.g. production) - the title
// stays "<original title>".
Aetheus.setTitlePrefix = function (prefix) {
    const pfx = (prefix || '').trim();
    if (!pfx) return;

    const apply = function () {
        const current = document.title || '';
        if (!current.startsWith(pfx + ' ')) {
            // Re-assigning document.title triggers the observer once more, but the guard above
            // makes that pass a no-op, so there is no infinite loop.
            document.title = pfx + ' ' + current;
        }
    };

    apply();

    // Observe <head> (it persists) rather than the <title> element directly: Blazor's HeadOutlet
    // can REPLACE the title node on navigation, which would silently detach an observer bound to
    // the old node. Watching the head subtree catches both text edits and node swaps.
    if (window._aetheusTitlePrefixObserver) {
        window._aetheusTitlePrefixObserver.disconnect();
    }
    const observer = new MutationObserver(apply);
    observer.observe(document.head, { childList: true, subtree: true, characterData: true });
    window._aetheusTitlePrefixObserver = observer;
};

Aetheus.focusById = function (id) {
    const el = document.getElementById(id);
    if (!el) return;
    // The id may point at a wrapper (e.g. a RadzenPassword/RadzenFormField) whose
    // real focusable target is a nested <input>; prefer that when present.
    const target = el.matches('input, textarea, select') ? el : (el.querySelector('input, textarea, select') || el);
    target.focus();
};

Aetheus.copyToClipboard = async function (text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
};

// Scroll a log terminal element to its bottom / top. Used by the pipeline run log viewer for
// auto-follow and the "jump to top/bottom" controls. Works with a virtualized child list because
// the virtualization spacer makes scrollHeight reflect the full (virtual) content height.
Aetheus.scrollToBottom = function (el) {
    if (!el) { return; }
    // Scroll once, then again on the next two frames: the virtualized list renders more rows as the
    // scroll position moves, growing scrollHeight, so a single assignment lands short of the true tail.
    el.scrollTop = el.scrollHeight;
    requestAnimationFrame(function () {
        el.scrollTop = el.scrollHeight;
        requestAnimationFrame(function () { el.scrollTop = el.scrollHeight; });
    });
};
Aetheus.scrollToTop = function (el) {
    if (el) { el.scrollTop = 0; }
};

Aetheus.initFormShortcuts = function () {
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
            // Prefer dialog forms (modal) over page forms
            var form = document.querySelector('.rz-dialog form') || document.querySelector('form');
            if (form) {
                var submitBtn = form.querySelector('button[type="submit"]');
                if (submitBtn && !submitBtn.disabled) {
                    e.preventDefault();
                    submitBtn.click();
                }
            }
        }
    });
};

Aetheus.trapFocus = function (containerSelector) {
    const container = document.querySelector(containerSelector);
    if (!container) return;

    const focusable = container.querySelectorAll(
        '[role="button"], button, a, input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );
    if (focusable.length === 0) return;

    focusable[0].focus();
};

// Lightweight localStorage helpers used by ActiveOrganizationService and any other client
// code that wants to persist a small string scalar across reloads. We swallow exceptions
// because Safari private mode and some embedded contexts throw on access.
Aetheus.getLocal = function (key) {
    try { return localStorage.getItem(key); } catch { return null; }
};

Aetheus.setLocal = function (key, value) {
    try {
        if (value === null || value === undefined) localStorage.removeItem(key);
        else localStorage.setItem(key, value);
    } catch { /* ignore */ }
};

// Compact drawer viewport watcher (Astraia parity). A matchMedia("(max-width: 1024px)") listener
// pushes the mobile/desktop boolean into MainLayout via [JSInvokable] OnViewportChanged, so the
// sidebar collapses to an overlay on phones/tablets and restores the in-flow rail on wider screens.
Aetheus._viewportMql = null;
Aetheus._viewportRef = null;
Aetheus._onViewportChange = function (e) {
    if (Aetheus._viewportRef) {
        Aetheus._viewportRef.invokeMethodAsync('OnViewportChanged', e.matches);
    }
};
Aetheus.watchViewport = function (dotNetRef) {
    Aetheus._viewportRef = dotNetRef;
    Aetheus._viewportMql = window.matchMedia('(max-width: 1024px)');
    Aetheus._viewportMql.addEventListener('change', Aetheus._onViewportChange);
    // Return the promise: MainLayout keeps the opaque splash mounted until this initial state has
    // been applied and rendered, eliminating the cold-load drawer/backdrop flash.
    return dotNetRef.invokeMethodAsync('OnViewportChanged', Aetheus._viewportMql.matches);
};
Aetheus.disposeViewportWatcher = function () {
    if (Aetheus._viewportMql) {
        Aetheus._viewportMql.removeEventListener('change', Aetheus._onViewportChange);
        Aetheus._viewportMql = null;
    }
    Aetheus._viewportRef = null;
};

// Data-grid actions are visually icon-only in compact rows. Radzen keeps their localized Text in
// the DOM for accessibility, but does not copy it to `title`, so mouse users get no tooltip. Apply
// the same contract to every current and future grid button, including Radzen's filter controls.
(function addDataGridButtonTooltips() {
    function apply(root) {
        const buttons = [];
        if (root instanceof Element && root.matches('.rz-datatable button')) buttons.push(root);
        if (root.querySelectorAll) buttons.push(...root.querySelectorAll('.rz-datatable button'));

        buttons.forEach(function (button) {
            if (button.getAttribute('title')) return;
            const text = button.querySelector('.rz-button-text')?.textContent?.trim();
            const label = text || button.getAttribute('aria-label');
            if (label) button.setAttribute('title', label);
        });
    }

    const observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
            mutation.addedNodes.forEach(function (node) {
                if (node.nodeType === Node.ELEMENT_NODE) apply(node);
            });
        });
    });

    function start() {
        apply(document);
        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();

function downloadBlob(filename, blob) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

window.downloadFile = function (filename, content, mimeType) {
    downloadBlob(filename, new Blob([content], { type: mimeType }));
};

window.downloadFileFromBytes = function (bytes, filename, mimeType) {
    downloadBlob(filename, new Blob([new Uint8Array(bytes)], { type: mimeType || 'application/octet-stream' }));
};

window.downloadFileFromStream = async function (filename, streamRef) {
    const data = await streamRef.arrayBuffer();
    downloadBlob(filename, new Blob([data], { type: 'application/octet-stream' }));
};

window.copyToClipboard = async function (text) {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // fall through
    }
    try {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        const ok = document.execCommand('copy');
        document.body.removeChild(ta);
        return ok;
    } catch {
        return false;
    }
};

// ESC4: close the top-most open dialog on Escape, mirroring the title-bar close button. Radzen only
// wires its own Escape handler when a dialog opts in (closeDialogOnEsc) - ours don't - so this gives
// Escape-to-close uniformly without editing every DialogService call site. A dialog with no close
// button (intentionally non-dismissible) has nothing to click and stays open. If a Radzen popup /
// dropdown is open we bail so Escape closes that first, not the whole dialog.
(function closeDialogsOnEscape() {
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape' && e.key !== 'Esc') return;
        if (document.querySelector('.rz-popup, .rz-overlaypanel, .rz-dropdown-panel, .rz-multiselect-panel, .rz-autocomplete-panel')) return;
        // Scope to the TOP-MOST dialog only and click ITS own close button. If that dialog has no
        // close button (e.g. a non-dismissible confirm stacked over an editor), do nothing - never
        // fall through to a dialog beneath it, which would close the wrong one.
        var dialogs = document.querySelectorAll('.rz-dialog');
        if (!dialogs.length) return;
        var closeBtn = dialogs[dialogs.length - 1].querySelector('.rz-dialog-titlebar-close');
        if (!closeBtn) return;
        e.preventDefault();
        closeBtn.click();
    });
})();

(function lockNavGroupsOpen() {
    function forceOpenGroup(group) {
        if (!group) return;

        group.setAttribute('aria-expanded', 'true');
        group.classList.add('rz-navigation-item-expanded');

        const submenuId = group.getAttribute('aria-controls');
        if (!submenuId) return;

        const submenu = document.getElementById(submenuId);
        if (!submenu) return;

        submenu.style.display = '';
        submenu.hidden = false;
        submenu.setAttribute('aria-hidden', 'false');
    }

    function forceAll() {
        document
            .querySelectorAll('li.rz-navigation-item.nav-group')
            .forEach(forceOpenGroup);
    }

    document.addEventListener('click', function (evt) {
        const groupLink = evt.target.closest('li.rz-navigation-item.nav-group > .rz-navigation-item-wrapper > a.rz-navigation-item-link');
        if (!groupLink) return;

        evt.preventDefault();
        evt.stopPropagation();
        if (typeof evt.stopImmediatePropagation === 'function') {
            evt.stopImmediatePropagation();
        }

        const targetHref = groupLink.getAttribute('href');
        if (!targetHref) return;

        const nextUrl = new URL(targetHref, window.location.origin);
        history.pushState({}, '', nextUrl.pathname + nextUrl.search + nextUrl.hash);
        window.dispatchEvent(new PopStateEvent('popstate'));

        const group = groupLink.closest('li.rz-navigation-item.nav-group');
        requestAnimationFrame(function () {
            forceOpenGroup(group);
        });
    }, true);

    const observer = new MutationObserver(function () {
        forceAll();
    });

    observer.observe(document.body, { childList: true, subtree: true });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', forceAll);
    } else {
        forceAll();
    }
})();
