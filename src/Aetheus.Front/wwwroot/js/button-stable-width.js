// Stabilizes Radzen button width when IsBusy toggles "rz-state-loading".
// Without this, switching from text -> spinner shrinks the button visibly.
// Strategy: track the natural width of every .rz-button while it is NOT loading,
// then pin that width as min-width as soon as the loading class is added.
(function () {
    if (typeof window === 'undefined' || !('MutationObserver' in window)) return;

    const NATURAL_KEY = 'data-natural-width';

    function recordNaturalWidth(btn) {
        if (!(btn instanceof HTMLElement)) return;
        if (btn.classList.contains('rz-state-loading')) return;
        if (btn.classList.contains('rz-button-icon-only')) return;
        const w = btn.getBoundingClientRect().width;
        if (w > 0) btn.setAttribute(NATURAL_KEY, Math.ceil(w));
    }

    function applyPin(btn) {
        if (!(btn instanceof HTMLElement)) return;
        if (btn.classList.contains('rz-state-loading')) {
            const natural = btn.getAttribute(NATURAL_KEY);
            if (natural) {
                btn.style.minWidth = natural + 'px';
            }
        } else {
            btn.style.minWidth = '';
        }
    }

    function scan(root) {
        const buttons = root.querySelectorAll
            ? root.querySelectorAll('.rz-button')
            : [];
        buttons.forEach(b => {
            recordNaturalWidth(b);
            applyPin(b);
        });
    }

    const classObserver = new MutationObserver(mutations => {
        for (const m of mutations) {
            if (m.type !== 'attributes' || m.attributeName !== 'class') continue;
            const btn = m.target;
            if (!(btn instanceof HTMLElement)) continue;
            if (!btn.classList.contains('rz-button')) continue;
            applyPin(btn);
        }
    });

    const treeObserver = new MutationObserver(muts => {
        for (const m of muts) {
            m.addedNodes.forEach(node => {
                if (!(node instanceof HTMLElement)) return;
                if (node.classList && node.classList.contains('rz-button')) {
                    recordNaturalWidth(node);
                }
                scan(node);
            });
        }
    });

    function init() {
        scan(document);
        classObserver.observe(document.body, {
            attributes: true,
            attributeFilter: ['class'],
            subtree: true
        });
        treeObserver.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
