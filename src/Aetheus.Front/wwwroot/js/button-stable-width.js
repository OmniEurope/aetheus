// Stabilizes Radzen button dimensions when IsBusy toggles "rz-state-loading".
// Without this, switching from the label to the spinner can shrink the button visibly.
(function () {
    if (typeof window === 'undefined' || !('MutationObserver' in window)) return;

    const NATURAL_WIDTH = 'data-natural-width';
    const NATURAL_HEIGHT = 'data-natural-height';
    const INLINE_WIDTH = 'data-natural-inline-width';
    const INLINE_HEIGHT = 'data-natural-inline-height';

    function isTrackable(button) {
        return button instanceof HTMLElement
            && button.classList.contains('rz-button')
            && !button.classList.contains('rz-button-icon-only');
    }

    function recordNaturalSize(button) {
        if (!isTrackable(button) || button.classList.contains('rz-state-loading')) return;
        const rect = button.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        button.setAttribute(NATURAL_WIDTH, Math.ceil(rect.width));
        button.setAttribute(NATURAL_HEIGHT, Math.ceil(rect.height));
        if (!button.hasAttribute(INLINE_WIDTH))
            button.setAttribute(INLINE_WIDTH, button.style.width);
        if (!button.hasAttribute(INLINE_HEIGHT))
            button.setAttribute(INLINE_HEIGHT, button.style.height);
    }

    function applyStableSize(button) {
        if (!isTrackable(button)) return;
        if (button.classList.contains('rz-state-loading')) {
            const width = button.getAttribute(NATURAL_WIDTH);
            const height = button.getAttribute(NATURAL_HEIGHT);
            if (width) button.style.width = `${width}px`;
            if (height) button.style.height = `${height}px`;
            return;
        }

        if (button.hasAttribute(INLINE_WIDTH))
            button.style.width = button.getAttribute(INLINE_WIDTH) || '';
        if (button.hasAttribute(INLINE_HEIGHT))
            button.style.height = button.getAttribute(INLINE_HEIGHT) || '';
        recordNaturalSize(button);
    }

    const resizeObserver = 'ResizeObserver' in window
        ? new ResizeObserver(entries => {
            for (const entry of entries)
                recordNaturalSize(entry.target);
        })
        : null;

    function register(root) {
        const buttons = [];
        if (isTrackable(root)) buttons.push(root);
        if (root.querySelectorAll)
            buttons.push(...root.querySelectorAll('.rz-button'));
        for (const button of buttons) {
            recordNaturalSize(button);
            applyStableSize(button);
            resizeObserver?.observe(button);
        }
    }

    function init() {
        register(document);
        const observer = new MutationObserver(mutations => {
            for (const mutation of mutations) {
                if (mutation.type === 'attributes') {
                    applyStableSize(mutation.target);
                    continue;
                }
                mutation.addedNodes.forEach(node => {
                    if (node instanceof HTMLElement) register(node);
                });
            }
        });
        observer.observe(document.body, {
            attributes: true,
            attributeFilter: ['class'],
            childList: true,
            subtree: true
        });
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', init, { once: true });
    else
        init();
})();
