const frames = new WeakMap();

export function setHeight(root, height) {
    if (!root) return;
    if (height) root.style.setProperty('--omni-code-editor-height', height);
    else root.style.removeProperty('--omni-code-editor-height');
}

export async function load(basePath) {
    try {
        const frame = await fetch('/omni-monaco-frame.html', { method: 'HEAD', cache: 'no-store' });
        const loader = await fetch(new URL(`${String(basePath).replace(/\/+$/, '')}/loader.js`, document.baseURI),
            { method: 'HEAD' });
        return frame.ok && loader.ok;
    } catch {
        return false;
    }
}

export async function mount(host, dotnet, options) {
    if (!host || frames.has(host)) return false;

    const frame = document.createElement('iframe');
    frame.className = 'omni-code-editor-frame';
    frame.title = options?.label || document.title;
    const state = { frame, module: null, root: null, observer: null };
    frames.set(host, state);

    const loaded = new Promise((resolve, reject) => {
        frame.onload = () => {
            const module = frame.contentWindow?.omniFrameEditor;
            if (module) resolve(module);
            else reject(new Error('OmniEurope editor frame did not load'));
        };
        frame.onerror = () => reject(new Error('OmniEurope editor frame could not load'));
    });
    frame.src = '/omni-monaco-frame.html';
    host.replaceChildren(frame);

    try {
        const module = await loaded;
        if (frames.get(host) !== state) return false;
        state.module = module;
        state.root = frame.contentWindow.document.getElementById('omni-code-root');
        if (!state.root) throw new Error('OmniEurope editor frame has no root');
        followTheme(host, state);
        if (!await module.load(options.monacoPath, options.culture, options.loadTimeoutMilliseconds))
            throw new Error('Monaco could not load inside the editor frame');
        if (!module.mount(state.root, dotnet, options))
            throw new Error('Monaco could not mount inside the editor frame');
        return true;
    } catch {
        dispose(host);
        return false;
    }
}

// The frame has its own document: mirror the host's theme and surface colour into it.
function followTheme(host, state) {
    const scope = host.closest('[data-omni-theme]');
    const syncTheme = () => {
        state.root.ownerDocument.documentElement.setAttribute('data-omni-theme',
            scope?.getAttribute('data-omni-theme') || 'dark');
        const surface = host.closest('.omni-code-editor') || host;
        state.root.style.backgroundColor = getComputedStyle(surface).backgroundColor;
    };
    syncTheme();
    if (scope) {
        state.observer = new MutationObserver(syncTheme);
        state.observer.observe(scope, { attributes: true, attributeFilter: ['data-omni-theme', 'style', 'class'] });
    }
}

export function setValue(host, value) {
    const state = frames.get(host);
    if (state?.module) state.module.setValue(state.root, value);
}

export function configure(host, options) {
    const state = frames.get(host);
    if (state?.module) state.module.configure(state.root, options);
}

export function read(host) {
    const state = frames.get(host);
    return state?.module?.read(state.root) ?? null;
}

export function focus(host) {
    const state = frames.get(host);
    if (state?.module) state.module.focus(state.root);
}

export function dispose(host) {
    const state = frames.get(host);
    if (!state) return;
    frames.delete(host);
    state.observer?.disconnect();
    if (state.module && state.root) state.module.dispose(state.root);
    state.frame.remove();
}
