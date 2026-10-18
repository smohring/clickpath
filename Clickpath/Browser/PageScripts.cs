using System.Text.Json;
using Clickpath.Models;

namespace Clickpath.Browser;

/// <summary>JavaScript snippets injected into pages. Arguments are substituted as JSON literals.</summary>
static class PageScripts
{
    static string J(string s) => JsonSerializer.Serialize(s);

    // Defines `picked`: elements matching selector and text; with `deep`, only the innermost matches.
    const string Prelude = """
        const sel = __SEL__, pat = __PAT__, vis = __VIS__, deep = __DEEP__;
        let re = null;
        try { re = pat ? new RegExp(pat, __FLAGS__) : null; } catch (e) { return { error: 'Invalid pattern: ' + e.message }; }
        let all;
        try { all = Array.from(document.querySelectorAll(sel)); } catch (e) { return { error: 'Invalid selector: ' + e.message }; }
        const norm = s => (s || '').replace(/\s+/g, ' ').trim();
        const textOf = e => norm(e.innerText || e.textContent);
        const isVisible = e => {
            const r = e.getBoundingClientRect(), st = getComputedStyle(e);
            return r.width > 0 && r.height > 0 && st.visibility !== 'hidden' && st.display !== 'none';
        };
        const hits = all.filter(e => (!vis || isVisible(e)) && (!re || re.test(textOf(e))));
        const candidates = (deep && re) ? hits.filter(e => !hits.some(o => o !== e && e.contains(o))) : hits;
        // "near": keep a candidate only if its row - the largest ancestor that holds no other candidate - also shows the near text
        let nearRe = null;
        try { nearRe = __NEAR__ ? new RegExp(__NEAR__, 'i') : null; } catch (e) { return { error: 'Invalid pattern: ' + e.message }; }
        const evidence = root => {
            const parts = [root.innerText || root.textContent || ''];
            const nodes = [root, ...Array.from(root.querySelectorAll('*')).slice(0, 300)];
            for (const n of nodes) {
                for (const a of ['alt', 'title', 'aria-label', 'src', 'href', 'class', 'id', 'name', 'data-host']) {
                    const v = n.getAttribute && n.getAttribute(a);
                    if (v) parts.push(v);
                }
                const bg = n.nodeType === 1 ? getComputedStyle(n).backgroundImage : '';
                if (bg && bg !== 'none') parts.push(bg);
            }
            return parts.join(' ');
        };
        const rowOf = e => {
            let node = e;
            while (node.parentElement && node.parentElement !== document.body) {
                const up = node.parentElement;
                if (candidates.some(o => o !== e && up.contains(o))) break;
                node = up;
            }
            return node;
        };
        const picked = nearRe ? candidates.filter(e => nearRe.test(evidence(rowOf(e)))) : candidates;
        """;

    // Shared by the element picker and the click recorder.
    const string ElementHelpers = """
        const norm = s => (s || '').replace(/\s+/g, ' ').trim();
        const css = el => {
            if (el.id && document.querySelectorAll('#' + CSS.escape(el.id)).length === 1) return '#' + CSS.escape(el.id);
            return el.tagName.toLowerCase() + Array.from(el.classList).slice(0, 2).map(c => '.' + CSS.escape(c)).join('');
        };
        """;

    static string Build(string body, ElementQuery q, bool visibleOnly) =>
        ("(() => {" + Prelude + body + "})()")
        .Replace("__SEL__", J(q.Selector))
        .Replace("__PAT__", J(q.Pattern))
        .Replace("__VIS__", visibleOnly ? "true" : "false")
        .Replace("__DEEP__", q.Deepest ? "true" : "false")
        .Replace("__FLAGS__", q.MatchCase ? "''" : "'i'")
        .Replace("__NEAR__", J(q.Near))
        .Replace("__IDX__", Math.Max(0, q.Index).ToString());

    const string ItemsOf = "picked.map(e => ({ text: textOf(e), href: e.href || (e.closest('a') ? e.closest('a').href : '') }))";

    /// <summary>All matches as { items: [{ text, href }] }.</summary>
    public static string Find(ElementQuery q, bool visibleOnly) =>
        Build("return { items: " + ItemsOf + " };", q, visibleOnly);

    /// <summary>Outlines up to 50 matches (first one in orange, scrolled into view) for <paramref name="durationMs"/>; returns the matches.</summary>
    public static string Highlight(ElementQuery q, bool visibleOnly, int durationMs) => Build(
        """
        document.querySelectorAll('[data-lf-hl]').forEach(n => n.remove());
        if (picked[__IDX__]) picked[__IDX__].scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
        const boxes = [];
        picked.slice(0, 50).forEach((e, i) => {
            const r = e.getBoundingClientRect();
            if (!r.width && !r.height) return;
            const d = document.createElement('div');
            d.setAttribute('data-lf-hl', '1');
            const first = i === __IDX__;
            d.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;box-sizing:border-box;border-radius:6px;'
                + 'left:' + (r.left - 3) + 'px;top:' + (r.top - 3) + 'px;width:' + (r.width + 6) + 'px;height:' + (r.height + 6) + 'px;'
                + 'border:3px solid ' + (first ? '#e8590c' : '#1c7ed6') + ';background:' + (first ? 'rgba(232,89,12,.18)' : 'rgba(28,126,214,.12)');
            document.documentElement.appendChild(d);
        });
        setTimeout(() => document.querySelectorAll('[data-lf-hl]').forEach(n => n.remove()), __DUR__);
        return { items:
        """.Replace("__DUR__", durationMs.ToString()) + ItemsOf + " };", q, visibleOnly);

    /// <summary>Scrolls the first visible match into view; returns its centre, size and whether the centre is hit-testable.</summary>
    public static string Locate(ElementQuery q) => Build(
        """
        const el = picked[__IDX__];
        if (!el) return { found: false };
        el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
        const r = el.getBoundingClientRect();
        const x = r.left + r.width / 2, y = r.top + r.height / 2;
        const top = document.elementFromPoint(x, y);
        return { found: true, x: x, y: y, w: r.width, h: r.height, direct: !!top && (top === el || el.contains(top) || top.contains(el)) };
        """, q, true);

    /// <summary>Fallback when the element centre is covered: scripted click.</summary>
    public static string JsClick(ElementQuery q) => Build(
        "if (!picked[__IDX__]) return false; picked[__IDX__].click(); return true;", q, true);

    /// <summary>
    /// The address and a 32-bit hash of every distinct word of the visible text (up to 50 000), to tell whether script replaced the page content.
    /// Every word counts: a cap on the first N words (it was 600) hid new text further down a long page, which then measured 0 % change.
    /// </summary>
    public const string Snapshot = """
        (() => {
            const text = document.body ? document.body.innerText : '';
            const seen = new Set();
            for (const w of (text.toLowerCase().match(/[\p{L}\p{N}]{3,}/gu) || [])) {
                if (!/\p{L}/u.test(w)) continue;   // numbers only: a countdown would add a "new word" every second
                let h = 5381;
                for (let i = 0; i < w.length; i++) h = ((h * 33) ^ w.charCodeAt(i)) | 0;
                seen.add(h);
                if (seen.size >= 50000) break;
            }
            return { url: location.href, words: Array.from(seen), len: document.documentElement.outerHTML.length };
        })()
        """;

    /// <summary>
    /// The same words and source length as <see cref="Snapshot"/>, run inside a frame of the page (the page's own script cannot read other origins' frames);
    /// null for a frame too small to show anything (tracking pixels, hidden frames).
    /// </summary>
    public const string FrameSnapshot = """
        (() => {
            if (window.innerWidth * window.innerHeight < 30000 || !document.body) return null;
            const seen = new Set();
            for (const w of (document.body.innerText.toLowerCase().match(/[\p{L}\p{N}]{3,}/gu) || [])) {
                if (!/\p{L}/u.test(w)) continue;
                let h = 5381;
                for (let i = 0; i < w.length; i++) h = ((h * 33) ^ w.charCodeAt(i)) | 0;
                seen.add(h);
                if (seen.size >= 50000) break;
            }
            return { words: Array.from(seen), len: document.documentElement.outerHTML.length };
        })()
        """;

    /// <summary>
    /// What the page is made of, for the log when "end on page change" saw nothing: how much text, how many elements, shadow roots and frames (with their
    /// host and, when readable, how much text is in them) - text in another origin's frame or in a shadow root is not part of the page's own text.
    /// </summary>
    public const string Diagnose = """
        (() => {
            const frames = Array.from(document.querySelectorAll('iframe')).map(f => {
                let host = '';
                try { host = new URL(f.src, location.href).host; } catch (e) { }
                let len = -1;
                try { len = f.contentDocument && f.contentDocument.body ? f.contentDocument.body.innerText.length : -1; } catch (e) { }
                return (host || 'no address') + (len >= 0 ? ' (' + len + ' characters of text)' : ' (other origin, text not readable)');
            });
            let shadow = 0;
            document.querySelectorAll('*').forEach(e => { if (e.shadowRoot) shadow++; });
            const text = document.body ? document.body.innerText : '';
            return {
                textLength: text.length, elements: document.getElementsByTagName('*').length, shadow, frames,
                sample: text.replace(/\s+/g, ' ').trim().slice(0, 140),
            };
        })()
        """;

    /// <summary>
    /// Finds the "next page" control: a rel=next link, an element whose text/label is a "next" word or arrow, an element with a "next"
    /// class, or the item after the current one in a pagination bar. <paramref name="customText"/> (if any) replaces that guesswork.
    /// Returns { found, disabled, href, x, y, w, h } (x is -1 for a link without a visible element).
    /// </summary>
    public static string NextPage(string customText) => """
        (() => {
            const custom = __CUSTOM__;
            const norm = s => (s || '').replace(/\s+/g, ' ').trim().toLowerCase();
            const isVisible = e => {
                const r = e.getBoundingClientRect(), st = getComputedStyle(e);
                return r.width > 0 && r.height > 0 && st.visibility !== 'hidden' && st.display !== 'none';
            };
            const off = /(^|[\s_-])(disabled|inactive)([\s_-]|$)/i;
            const isDisabled = e => {
                const li = e.closest('li');
                return !!(e.disabled || e.getAttribute('aria-disabled') === 'true' || off.test(e.getAttribute('class') || '')
                    || (li && off.test(li.getAttribute('class') || '')));
            };
            const labelOf = e => norm(e.innerText || e.textContent || e.value || '');
            const arrows = /[›»‹«<>→←▶◀⟩⟨❯❮…\s.]+/g;
            const nextWords = /^(next|next page|weiter|nächste|nächste seite|siguiente|suivant|suivante|volgende|próxima|próximo|avanti|successiva)$/;
            const looksNext = e => {
                const t = labelOf(e), core = t.replace(arrows, ' ').trim();
                if (core === '' && /[›»>→▶⟩❯]/.test(t)) return true;
                if (nextWords.test(core)) return true;
                const hint = norm((e.getAttribute('aria-label') || '') + ' ' + (e.getAttribute('title') || ''));
                return /\bnext\b|\bweiter\b|nächste/.test(hint) && !/prev|zurück|vorherige/.test(hint);
            };
            const sel = 'a, button, [role=button], input[type=button], input[type=submit], li, span';
            const all = Array.from(document.querySelectorAll(sel)).filter(isVisible);
            let el = null;
            if (custom) {
                el = all.find(e => labelOf(e).includes(custom) || norm(e.getAttribute('aria-label')).includes(custom));
            } else {
                el = all.find(e => e.matches('a[rel~=next][href]'));
                if (!el) {
                    const link = document.querySelector('link[rel~=next][href], a[rel~=next][href]');
                    if (link) return { found: true, disabled: false, href: link.href, x: -1, y: -1, w: 0, h: 0 };
                }
                el = el || all.find(looksNext);
                el = el || all.find(e => e.matches('a, button, li') && /(^|[\s_-])next([\s_-]|$)/i.test(e.getAttribute('class') || '')
                    && !/prev/i.test(e.getAttribute('class') || ''));
                if (!el) {
                    const cur = document.querySelector('[class*=pagination i] [aria-current=page], [class*=pager i] [aria-current=page], '
                        + '.pagination .active, .pagination .current, .pager .active, .pager .current');
                    const item = cur && (cur.closest('li') || cur).nextElementSibling;
                    el = item && (item.matches('a, button') ? item : item.querySelector('a, button') || item);
                }
            }
            if (!el) return { found: false };
            const target = el.closest('a, button, [role=button]') || el.querySelector('a, button') || el;
            const disabled = isDisabled(el) || isDisabled(target);
            target.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
            const r = target.getBoundingClientRect();
            return { found: true, disabled: disabled, href: target.href || '', x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height };
        })()
        """.Replace("__CUSTOM__", J(customText.Trim().ToLowerInvariant()));

    /// <summary>A random viewport point (or null after 40 tries) where a click hits nothing clickable: no link, button, field, frame or pointer cursor.</summary>
    public const string EmptySpot = """
        (() => {
            const w = window.innerWidth, h = window.innerHeight;
            const clickable = 'a, button, input, select, textarea, label, summary, video, audio, iframe, embed, object, canvas, '
                + '[onclick], [role=button], [role=link], [role=tab], [role=menuitem], [contenteditable], [tabindex]';
            for (let i = 0; i < 40; i++) {
                const x = w * (0.05 + 0.9 * Math.random()), y = h * (0.05 + 0.9 * Math.random());
                const el = document.elementFromPoint(x, y);
                if (!el || el.closest(clickable) || getComputedStyle(el).cursor === 'pointer') continue;
                return { x: x, y: y };
            }
            return null;
        })()
        """;

    public const string ViewportSize = "({ w: window.innerWidth, h: window.innerHeight })";

    /// <summary>Shows a ring at the point where a click is about to happen.</summary>
    public static string FlashPoint(double x, double y, int durationMs) => """
        (() => {
            const d = document.createElement('div');
            d.setAttribute('data-lf-hl', '1');
            d.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;box-sizing:border-box;width:36px;height:36px;border-radius:50%;'
                + 'border:3px solid #e8590c;background:rgba(232,89,12,.25);left:__X__px;top:__Y__px;margin:-18px 0 0 -18px';
            document.documentElement.appendChild(d);
            setTimeout(() => d.remove(), __DUR__);
        })()
        """
        .Replace("__X__", x.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Replace("__Y__", y.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Replace("__DUR__", durationMs.ToString());

    /// <summary>Element picker: highlights on hover, reports the clicked element via postMessage.</summary>
    public static readonly string Pick = "(() => {" + ElementHelpers + """
        if (window.__lfPick) window.__lfPick.stop();
        const box = document.createElement('div');
        box.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;border:2px solid #e8590c;background:rgba(232,89,12,.15);display:none';
        document.documentElement.appendChild(box);
        const move = e => {
            const r = e.target.getBoundingClientRect();
            box.style.display = 'block';
            box.style.left = r.left + 'px'; box.style.top = r.top + 'px';
            box.style.width = r.width + 'px'; box.style.height = r.height + 'px';
        };
        const swallow = e => { e.preventDefault(); e.stopPropagation(); e.stopImmediatePropagation(); };
        const click = e => {
            swallow(e);
            const t = e.target, a = t.closest('a');
            stop();
            window.chrome.webview.postMessage({ kind: 'pick', selector: css(t), text: norm(t.innerText || t.textContent), href: a ? a.href : '' });
        };
        const key = e => { if (e.key === 'Escape') stop(); };
        const blocked = ['mousedown', 'mouseup', 'pointerdown', 'pointerup'];
        function stop() {
            document.removeEventListener('mouseover', move, true);
            document.removeEventListener('click', click, true);
            document.removeEventListener('keydown', key, true);
            blocked.forEach(n => document.removeEventListener(n, swallow, true));
            box.remove();
            delete window.__lfPick;
        }
        document.addEventListener('mouseover', move, true);
        document.addEventListener('click', click, true);
        document.addEventListener('keydown', key, true);
        blocked.forEach(n => document.addEventListener(n, swallow, true));
        window.__lfPick = { stop: stop };
        """ + "})()";

    /// <summary>Runs in every document and frame: reports real user clicks; the app decides whether it is recording.</summary>
    public static readonly string ClickRecorder = "(() => {" + ElementHelpers + """
        if (window.__lfClickHook) return;
        window.__lfClickHook = true;
        document.addEventListener('click', e => {
            if (!e.isTrusted || window.__lfPick || !(e.target instanceof Element)) return;
            const el = e.target.closest('a, button, [role=button], [role=link], [role=tab], summary, [onclick]') || e.target;
            const a = el.closest('a[href]');
            window.chrome.webview.postMessage({
                kind: 'click', selector: css(el), isLink: !!a, href: a ? a.href : '',
                text: norm(el.innerText || el.textContent || el.value || el.getAttribute('aria-label'))
            });
        }, true);
        """ + "})()";
}
