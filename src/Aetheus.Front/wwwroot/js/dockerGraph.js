// dockerGraph.js - Interactive SVG node graph for Docker Relations
// Inspired by Pronoia's mindmap engine. Read-only, with pan/zoom/drag and click callbacks.

const NS = 'http://www.w3.org/2000/svg';

const GROUP_COLORS = {
    project:   { bg: '#e3f2fd', border: '#1976d2', text: '#0d47a1' },
    container: { bg: '#c8e6c9', border: '#43a047', text: '#1b5e20' },
    image:     { bg: '#fff9c4', border: '#fbc02d', text: '#827717' },
    network:   { bg: '#d1c4e9', border: '#7b1fa2', text: '#4a148c' },
    volume:    { bg: '#ffe0b2', border: '#fb8c00', text: '#e65100' },
    stopped:   { bg: '#eceff1', border: '#90a4ae', text: '#37474f' }
};

function el(tag) { return document.createElementNS(NS, tag); }
function getColor(group) { return GROUP_COLORS[group] || GROUP_COLORS.container; }

const instances = {};

export function initDockerGraph(svgId, dotNetRef, dataJson, viewKey) {
    dispose(svgId);
    const svg = document.getElementById(svgId);
    if (!svg) return;

    const S = {
        nodes: [], edges: [],
        panX: 0, panY: 0, zoom: 1,
        isPanning: false, panStartX: 0, panStartY: 0,
        dragNode: null, dragOffX: 0, dragOffY: 0, dragMoved: false
    };

    while (svg.firstChild) svg.removeChild(svg.firstChild);

    const defs = el('defs');
    const mkr = el('marker');
    mkr.setAttribute('id', `ah-${svgId}`);
    mkr.setAttribute('markerWidth', '10');
    mkr.setAttribute('markerHeight', '7');
    mkr.setAttribute('refX', '10');
    mkr.setAttribute('refY', '3.5');
    mkr.setAttribute('orient', 'auto');
    const poly = el('polygon');
    poly.setAttribute('points', '0 0, 10 3.5, 0 7');
    poly.setAttribute('fill', '#888');
    mkr.appendChild(poly);
    defs.appendChild(mkr);
    svg.appendChild(defs);

    const gMain = el('g');
    svg.appendChild(gMain);
    const gEdges = el('g'); gMain.appendChild(gEdges);
    const gNodes = el('g'); gMain.appendChild(gNodes);

    const measureText = el('text');
    measureText.setAttribute('font-size', '13');
    measureText.setAttribute('font-family', 'sans-serif');
    measureText.setAttribute('visibility', 'hidden');
    gMain.appendChild(measureText);

    function loadView() {
        if (!viewKey) return false;
        try {
            const raw = localStorage.getItem(viewKey);
            if (!raw) return false;
            const v = JSON.parse(raw);
            if (typeof v?.panX === 'number' && typeof v?.panY === 'number' && typeof v?.zoom === 'number') {
                S.panX = v.panX; S.panY = v.panY;
                S.zoom = Math.max(0.2, Math.min(3, v.zoom));
                return true;
            }
        } catch (e) { console.debug('dockerGraph: failed to load saved view', e); }
        return false;
    }
    function saveView() {
        if (!viewKey) return;
        try { localStorage.setItem(viewKey, JSON.stringify({ panX: S.panX, panY: S.panY, zoom: S.zoom })); }
        catch (e) { console.debug('dockerGraph: failed to persist view', e); }
    }
    function updateTransform() {
        gMain.setAttribute('transform', `translate(${S.panX},${S.panY}) scale(${S.zoom})`);
    }

    function loadData(json) {
        let d;
        try { d = typeof json === 'string' ? JSON.parse(json) : json; } catch (_) { d = {}; }
        S.nodes = (d.nodes || []).map(n => ({
            id: n.id, label: n.label || '', group: n.group || 'container',
            data: n.data ?? null,
            x: n.x ?? 0, y: n.y ?? 0, w: 0, h: 0
        }));
        S.edges = (d.edges || []).map(e => ({ from: e.from, to: e.to }));
        if (S.nodes.every(n => n.x === 0 && n.y === 0)) autoLayout();
        if (!loadView()) centerView();
        render();
    }

    // Layered radial layout: project hubs in a ring, satellites around each hub.
    function autoLayout() {
        const r = svg.getBoundingClientRect();
        const cw = Math.max(r.width, 800);
        const ch = Math.max(r.height, 600);
        const projects = S.nodes.filter(n => n.group === 'project');
        if (projects.length === 0) {
            // No projects: simple grid.
            const cols = Math.ceil(Math.sqrt(S.nodes.length));
            S.nodes.forEach((n, i) => {
                n.x = 80 + (i % cols) * 180;
                n.y = 80 + Math.floor(i / cols) * 100;
            });
            return;
        }
        const cx = cw / 2, cy = ch / 2;
        const R = Math.min(cw, ch) * 0.30 + projects.length * 40;
        projects.forEach((p, i) => {
            const a = (i / projects.length) * Math.PI * 2 - Math.PI / 2;
            p.x = cx + Math.cos(a) * (projects.length === 1 ? 0 : R);
            p.y = cy + Math.sin(a) * (projects.length === 1 ? 0 : R);
        });

        // Build adjacency project -> satellites
        projects.forEach(p => {
            const sats = S.edges.filter(e => e.from === p.id || e.to === p.id)
                .map(e => e.from === p.id ? e.to : e.from)
                .map(id => S.nodes.find(n => n.id === id))
                .filter(n => n && n.group !== 'project');
            const unique = [...new Map(sats.map(n => [n.id, n])).values()];
            const ringR = 140 + unique.length * 6;
            unique.forEach((n, i) => {
                const a = (i / unique.length) * Math.PI * 2;
                n.x = p.x + Math.cos(a) * ringR;
                n.y = p.y + Math.sin(a) * ringR;
            });
        });

        // Orphans
        let ox = 60, oy = 60;
        S.nodes.forEach(n => {
            if (n.x === 0 && n.y === 0) { n.x = ox; n.y = oy; ox += 160; if (ox > cw - 80) { ox = 60; oy += 80; } }
        });
    }

    function centerView() {
        if (S.nodes.length === 0) return;
        const xs = S.nodes.map(n => n.x), ys = S.nodes.map(n => n.y);
        const minX = Math.min(...xs), maxX = Math.max(...xs);
        const minY = Math.min(...ys), maxY = Math.max(...ys);
        const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        const r = svg.getBoundingClientRect();
        S.zoom = Math.min(1, Math.min(r.width / Math.max(maxX - minX + 200, 1), r.height / Math.max(maxY - minY + 200, 1)));
        S.panX = r.width / 2 - cx * S.zoom;
        S.panY = r.height / 2 - cy * S.zoom;
    }

    function render() {
        while (gEdges.firstChild) gEdges.removeChild(gEdges.firstChild);
        while (gNodes.firstChild) gNodes.removeChild(gNodes.firstChild);
        updateTransform();

        S.edges.forEach(e => {
            const fn = S.nodes.find(n => n.id === e.from);
            const tn = S.nodes.find(n => n.id === e.to);
            if (!fn || !tn) return;
            const dx = tn.x - fn.x;
            const cx1 = fn.x + dx * 0.4, cy1 = fn.y;
            const cx2 = fn.x + dx * 0.6, cy2 = tn.y;
            const d = `M ${fn.x} ${fn.y} C ${cx1} ${cy1}, ${cx2} ${cy2}, ${tn.x} ${tn.y}`;
            const line = el('path');
            line.setAttribute('d', d);
            line.setAttribute('stroke', '#aaa');
            line.setAttribute('stroke-width', '1.5');
            line.setAttribute('fill', 'none');
            line.setAttribute('marker-end', `url(#ah-${svgId})`);
            gEdges.appendChild(line);
        });

        S.nodes.forEach(n => {
            const c = getColor(n.group);
            const g = el('g');
            g.setAttribute('data-node', n.id);
            g.style.cursor = 'pointer';

            measureText.textContent = n.label;
            const bb = measureText.getBBox();
            const padX = 14, padY = 8;
            const w = Math.max(bb.width + padX * 2, n.group === 'project' ? 120 : 80);
            const h = bb.height + padY * 2;
            n.w = w; n.h = h;

            const rect = el('rect');
            rect.setAttribute('x', n.x - w / 2);
            rect.setAttribute('y', n.y - h / 2);
            rect.setAttribute('width', w);
            rect.setAttribute('height', h);
            rect.setAttribute('rx', n.group === 'project' ? '14' : '8');
            rect.setAttribute('ry', n.group === 'project' ? '14' : '8');
            rect.setAttribute('fill', c.bg);
            rect.setAttribute('stroke', c.border);
            rect.setAttribute('stroke-width', n.group === 'project' ? '2.5' : '1.5');
            g.appendChild(rect);

            const txt = el('text');
            txt.setAttribute('x', n.x);
            txt.setAttribute('y', n.y);
            txt.setAttribute('text-anchor', 'middle');
            txt.setAttribute('dominant-baseline', 'central');
            txt.setAttribute('font-size', '13');
            txt.setAttribute('font-family', 'sans-serif');
            txt.setAttribute('font-weight', n.group === 'project' ? 'bold' : 'normal');
            txt.setAttribute('fill', c.text);
            txt.setAttribute('pointer-events', 'none');
            txt.textContent = n.label;
            g.appendChild(txt);

            gNodes.appendChild(g);
        });
    }

    function svgPt(evt) {
        const r = svg.getBoundingClientRect();
        return {
            x: (evt.clientX - r.left - S.panX) / S.zoom,
            y: (evt.clientY - r.top - S.panY) / S.zoom
        };
    }

    function findNodeFromTarget(t) {
        let cur = t;
        while (cur && cur !== svg) {
            if (cur.getAttribute && cur.getAttribute('data-node')) {
                const id = cur.getAttribute('data-node');
                return S.nodes.find(n => n.id === id);
            }
            cur = cur.parentNode;
        }
        return null;
    }

    function onMouseDown(e) {
        const node = findNodeFromTarget(e.target);
        if (node) {
            S.dragNode = node;
            const p = svgPt(e);
            S.dragOffX = p.x - node.x;
            S.dragOffY = p.y - node.y;
            S.dragMoved = false;
        } else {
            S.isPanning = true;
            S.panStartX = e.clientX - S.panX;
            S.panStartY = e.clientY - S.panY;
        }
    }
    function onMouseMove(e) {
        if (S.dragNode) {
            const p = svgPt(e);
            S.dragNode.x = p.x - S.dragOffX;
            S.dragNode.y = p.y - S.dragOffY;
            S.dragMoved = true;
            render();
        } else if (S.isPanning) {
            S.panX = e.clientX - S.panStartX;
            S.panY = e.clientY - S.panStartY;
            updateTransform();
        }
    }
    function onMouseUp(e) {
        if (S.dragNode && !S.dragMoved && dotNetRef) {
            dotNetRef.invokeMethodAsync('OnGraphNodeClicked', S.dragNode.id);
        }
        S.dragNode = null;
        if (S.isPanning) { S.isPanning = false; saveView(); }
    }
    function onWheel(e) {
        e.preventDefault();
        const r = svg.getBoundingClientRect();
        const mx = e.clientX - r.left, my = e.clientY - r.top;
        const wx = (mx - S.panX) / S.zoom, wy = (my - S.panY) / S.zoom;
        const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1;
        S.zoom = Math.max(0.2, Math.min(3, S.zoom * factor));
        S.panX = mx - wx * S.zoom;
        S.panY = my - wy * S.zoom;
        updateTransform();
        saveView();
    }

    svg.addEventListener('mousedown', onMouseDown);
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
    svg.addEventListener('wheel', onWheel, { passive: false });

    instances[svgId] = {
        dispose: () => {
            svg.removeEventListener('mousedown', onMouseDown);
            window.removeEventListener('mousemove', onMouseMove);
            window.removeEventListener('mouseup', onMouseUp);
            svg.removeEventListener('wheel', onWheel);
            while (svg.firstChild) svg.removeChild(svg.firstChild);
        },
        loadData,
        resetView: () => { S.panX = 0; S.panY = 0; S.zoom = 1; centerView(); updateTransform(); saveView(); }
    };

    loadData(dataJson);
}

export function setData(svgId, dataJson) {
    instances[svgId]?.loadData(dataJson);
}

export function resetView(svgId) {
    instances[svgId]?.resetView();
}

export function dispose(svgId) {
    if (instances[svgId]) {
        instances[svgId].dispose();
        delete instances[svgId];
    }
}
