// SPDX-License-Identifier: EUPL-1.2
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { JSDOM } = require('jsdom');

function loadScript(markup) {
    const dom = new JSDOM(`<!doctype html><html><body>${markup}</body></html>`, {
        url: 'https://localhost/'
    });
    global.window = dom.window;
    global.document = dom.window.document;
    global.HTMLElement = dom.window.HTMLElement;
    global.MutationObserver = dom.window.MutationObserver;
    const source = path.resolve(
        __dirname,
        '../../src/Aetheus.Front/wwwroot/js/button-stable-width.js');
    delete require.cache[source];
    require(source);
    dom.window.document.dispatchEvent(new dom.window.Event('DOMContentLoaded'));
    return dom;
}

test('loading button keeps its measured width and height then restores inline sizing', async () => {
    const dom = loadScript('<button class="rz-button" style="width: 9rem">Save</button>');
    const button = document.querySelector('button');
    button.getBoundingClientRect = () => ({
        width: 144,
        height: 40,
        top: 0,
        right: 144,
        bottom: 40,
        left: 0,
        x: 0,
        y: 0,
        toJSON() {}
    });

    button.classList.add('measured');
    await new Promise(resolve => dom.window.setTimeout(resolve, 0));
    button.classList.add('rz-state-loading');
    await new Promise(resolve => dom.window.setTimeout(resolve, 0));

    assert.equal(button.style.width, '144px');
    assert.equal(button.style.height, '40px');

    button.classList.remove('rz-state-loading');
    await new Promise(resolve => dom.window.setTimeout(resolve, 0));
    assert.equal(button.style.width, '9rem');
    assert.equal(button.style.height, '');
    dom.window.close();
});
