const monacoFrames = new Map();

async function createMonacoFrame(elementId) {
    const container = document.getElementById(elementId);
    if (!container) return null;

    disposeMonacoFrame(elementId);
    const frame = document.createElement('iframe');
    frame.className = 'monaco-editor-frame';
    frame.title = container.getAttribute('aria-label') || document.title;
    const loaded = new Promise((resolve, reject) => {
        frame.onload = () => {
            const interop = frame.contentWindow?.monacoInterop;
            if (interop) resolve(interop);
            else reject(new Error('Monaco frame did not load its editor script'));
        };
        frame.onerror = () => reject(new Error('Monaco frame could not load'));
    });

    monacoFrames.set(elementId, frame);
    frame.src = '/monaco-frame.html';
    container.replaceChildren(frame);
    try {
        return await loaded;
    } catch (error) {
        disposeMonacoFrame(elementId);
        throw error;
    }
}

function getMonacoFrame(elementId) {
    const interop = monacoFrames.get(elementId)?.contentWindow?.monacoInterop;
    if (!interop) throw new Error('Monaco editor is not ready');
    return interop;
}

function disposeMonacoFrame(elementId) {
    const frame = monacoFrames.get(elementId);
    if (!frame) return;
    monacoFrames.delete(elementId);
    frame.remove();
}

function eachMonacoFrame(callback) {
    for (const frame of monacoFrames.values()) {
        const interop = frame.contentWindow?.monacoInterop;
        if (interop) callback(interop);
    }
}

window.monacoInterop = {
    init: async function (elementId, yamlContent, dotNetRef, isDark) {
        const editor = await createMonacoFrame(elementId);
        if (editor) await editor.init('monaco-root', yamlContent, dotNetRef, isDark);
    },

    getValue: function (elementId) {
        return getMonacoFrame(elementId).getValue('monaco-root');
    },

    setValue: function (elementId, content) {
        getMonacoFrame(elementId).setValue('monaco-root', content);
    },

    setTheme: function (isDark) {
        eachMonacoFrame(editor => editor.setTheme(isDark));
    },

    setValidationErrors: function (elementId, errors) {
        getMonacoFrame(elementId).setValidationErrors('monaco-root', errors);
    },

    clearValidationErrors: function (elementId) {
        getMonacoFrame(elementId).clearValidationErrors('monaco-root');
    },

    registerCompletionProvider: function (suggestions) {
        eachMonacoFrame(editor => editor.registerCompletionProvider(suggestions));
    },

    updateSuggestions: function (suggestions) {
        eachMonacoFrame(editor => editor.updateSuggestions(suggestions));
    },

    formatDocument: function (elementId) {
        getMonacoFrame(elementId).formatDocument('monaco-root');
    },

    dispose: function (elementId) {
        getMonacoFrame(elementId).dispose('monaco-root');
        disposeMonacoFrame(elementId);
    },

    initDiffEditor: async function (elementId, originalContent, modifiedContent, isDark) {
        const editor = await createMonacoFrame(elementId);
        if (editor) await editor.initDiffEditor('monaco-root', originalContent, modifiedContent, isDark);
    },

    updateDiffEditor: function (elementId, originalContent, modifiedContent) {
        getMonacoFrame(elementId).updateDiffEditor('monaco-root', originalContent, modifiedContent);
    },

    disposeDiffEditor: function (elementId) {
        getMonacoFrame(elementId).disposeDiffEditor('monaco-root');
        disposeMonacoFrame(elementId);
    },

    initReadOnly: async function (elementId, content, language, isDark, lineNumber) {
        const editor = await createMonacoFrame(elementId);
        if (editor) await editor.initReadOnly('monaco-root', content, language, isDark, lineNumber);
    },

    disposeEditor: function (elementId) {
        getMonacoFrame(elementId).disposeEditor('monaco-root');
        disposeMonacoFrame(elementId);
    }
};
