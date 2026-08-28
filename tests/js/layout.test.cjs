// SPDX-License-Identifier: EUPL-1.2
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { JSDOM } = require('jsdom');

function loadLayout(markup = '<div id="app-splash"></div><link href="material-base.css">') {
    const dom = new JSDOM(`<!doctype html><html><head><title>Aetheus</title><link href="material-base.css"></head><body>${markup}</body></html>`, {
        url: 'https://localhost/'
    });
    global.window = dom.window;
    global.document = dom.window.document;
    global.navigator = dom.window.navigator;
    global.MutationObserver = dom.window.MutationObserver;
    global.requestAnimationFrame = callback => callback();
    global.Aetheus = {};
    dom.window.Aetheus = global.Aetheus;
    const source = path.resolve(__dirname, '../../src/Aetheus.Front/wwwroot/js/layout.js');
    delete require.cache[source];
    require(source);
    return dom;
}

test('layout helpers update language, theme and remove the splash idempotently', () => {
    const dom = loadLayout();
    Aetheus.setLang('fr-BE');
    Aetheus.setTheme('/css/material-dark.css');
    Aetheus.hideSplash();
    document.getElementById('app-splash').dispatchEvent(new dom.window.Event('transitionend'));
    Aetheus.hideSplash();

    assert.equal(document.documentElement.lang, 'fr');
    assert.match(document.querySelector('link').href, /material-dark\.css$/);
    assert.equal(document.getElementById('app-splash'), null);
    dom.window.close();
});

test('clipboard helper reports provider success and failure honestly', async () => {
    const dom = loadLayout('');
    Object.defineProperty(global.navigator, 'clipboard', {
        configurable: true,
        value: { writeText: async value => assert.equal(value, 'expected') }
    });
    assert.equal(await Aetheus.copyToClipboard('expected'), true);
    Object.defineProperty(global.navigator, 'clipboard', {
        configurable: true,
        value: { writeText: async () => { throw new Error('denied'); } }
    });
    assert.equal(await Aetheus.copyToClipboard('expected'), false);
    dom.window.close();
});
