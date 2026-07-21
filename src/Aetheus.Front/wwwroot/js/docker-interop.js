window.dockerInterop = {
    scrollToBottom: function (elementClass) {
        const el = document.querySelector('.' + elementClass);
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
