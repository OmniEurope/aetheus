window.dockerInterop = {
    scrollToBottom: function (elementClass) {
        // The class sits on OE's code block; the scrolling element is the <pre> inside it.
        const block = document.querySelector('.' + elementClass);
        const el = block && (block.querySelector('pre') || block);
        if (el) el.scrollTop = el.scrollHeight;
    },
    copyToClipboard: async function (text) {
        if (navigator.clipboard) {
            await navigator.clipboard.writeText(text);
            return true;
        }
        return false;
    }
};
