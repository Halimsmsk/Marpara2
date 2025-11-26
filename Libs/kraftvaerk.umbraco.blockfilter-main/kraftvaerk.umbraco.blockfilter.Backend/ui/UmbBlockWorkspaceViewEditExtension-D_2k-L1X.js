import { a as c, x as u, i as h, t as m } from "./lit-element-FqBfIOu4.js";
var p = Object.defineProperty, b = Object.getOwnPropertyDescriptor, f = (t, o, r, e) => {
  for (var s = e > 1 ? void 0 : e ? b(o, r) : o, i = t.length - 1, a; i >= 0; i--)
    (a = t[i]) && (s = (e ? a(o, r, s) : a(s)) || s);
  return e && s && p(o, r, s), s;
};
let l = class extends c {
  constructor() {
    super(), this.initialized = !1, this.observers = [], this.staticLogoUrlPrimary = "/companentresim/logo.png", this.staticLogoUrlFallback = "/companentresim/singleVideo.png", this.seenModals = /* @__PURE__ */ new WeakSet(), this.observedHosts = /* @__PURE__ */ new WeakSet(), console.log("🚀 UmbBlockWorkspaceViewEditExtension: Constructor called");
  }
  connectedCallback() {
    super.connectedCallback(), this.initialized || (this.initialize(), this.initialized = !0);
  }
  disconnectedCallback() {
    super.disconnectedCallback(), this.observers.forEach((t) => t.disconnect()), this.observers = [], this.periodicCheckerId && window.clearInterval(this.periodicCheckerId), this.modalPollerId && window.clearInterval(this.modalPollerId);
  }
  initialize() {
    this.addGlobalStyles(), setTimeout(() => this.detectAndInject(), 100), setTimeout(() => this.detectAndInject(), 500), setTimeout(() => this.detectAndInject(), 1e3), setTimeout(() => this.detectAndInject(), 2e3), this.modalPollerId = window.setInterval(() => this.pollAndLogModals(), 800);
    const t = new MutationObserver((o) => {
      setTimeout(() => {
        this.detectAndInject(), this.detectWorkspaceEditorsAndInject();
      }, 50);
    });
    t.observe(document.body, { childList: !0, subtree: !0 }), this.observers.push(t), this.attachDeepObservers(), this.periodicCheckerId = window.setInterval(() => {
      this.attachDeepObservers(), this.detectAndInject(), this.detectWorkspaceEditorsAndInject();
    }, 3e3);
  }
  // Attach MutationObservers to modal hosts AND their shadowRoots so Shadow DOM mutations are caught
  attachDeepObservers() {
    const t = this.querySelectorAllDeep("umb-backoffice-modal-container, uui-modal-container, uui-modal-sidebar, umb-workspace-modal, uui-dialog, uui-modal, umb-workspace-editor");
    for (const o of t) {
      this.observeNode(o);
      const r = o.shadowRoot;
      r && this.observeNode(r);
    }
  }
  observeNode(t) {
    if (this.observedHosts.has(t)) return;
    this.observedHosts.add(t);
    const o = new MutationObserver((r) => {
      setTimeout(() => {
        this.detectWorkspaceEditorsAndInject(), this.detectAndInject();
      }, 30);
    });
    try {
      o.observe(t, { childList: !0, subtree: !0 }), this.observers.push(o), console.log("👁️ KV: observing", t instanceof Element ? t.tagName.toLowerCase() : "[shadow-root]", t);
    } catch {
    }
    if (t.querySelectorAll)
      try {
        t.querySelectorAll("*").forEach((e) => {
          e != null && e.shadowRoot && this.observeNode(e.shadowRoot);
        });
      } catch {
      }
  }
  querySelectorAllDeep(t, o = document) {
    const r = [], e = [], s = o instanceof Document ? o.documentElement : o;
    s && e.push(s);
    const i = /* @__PURE__ */ new Set();
    for (; e.length; ) {
      const a = e.shift();
      if (i.has(a)) continue;
      if (i.add(a), "querySelectorAll" in a)
        try {
          a.querySelectorAll(t).forEach((n) => r.push(n));
        } catch {
        }
      const d = (a instanceof ShadowRoot, Array.from(a.children));
      for (const n of d)
        e.push(n), n.shadowRoot && e.push(n.shadowRoot);
      a.shadowRoot && e.push(a.shadowRoot);
    }
    return Array.from(new Set(r));
  }
  queryInsideDeep(t, o) {
    const r = [], e = [];
    t.shadowRoot && e.push(t.shadowRoot), e.push(t);
    const s = /* @__PURE__ */ new Set();
    for (; e.length; ) {
      const i = e.shift();
      if (s.has(i)) continue;
      if (s.add(i), "querySelectorAll" in i)
        try {
          i.querySelectorAll(o).forEach((d) => r.push(d));
        } catch {
        }
      const a = (i instanceof ShadowRoot, i.children);
      Array.from(a).forEach((d) => {
        e.push(d), d.shadowRoot && e.push(d.shadowRoot);
      }), i.shadowRoot && e.push(i.shadowRoot);
    }
    return Array.from(new Set(r));
  }
  pollAndLogModals() {
    const t = [
      "umb-backoffice-modal-container",
      "uui-modal-container",
      "uui-modal-sidebar",
      "umb-workspace-modal",
      "umb-modal-dialog",
      "uui-dialog",
      "uui-modal",
      "umb-workspace-editor"
    ].join(","), o = this.querySelectorAllDeep(t);
    if (o.length !== 0)
      for (const r of o)
        this.seenModals.has(r) || (this.seenModals.add(r), console.log("🟢 Tespit:", r.tagName.toLowerCase(), r));
  }
  detectAndInject() {
    try {
      const t = this.querySelectorAllDeep("umb-backoffice-modal-container, uui-modal-container, uui-modal-sidebar, umb-workspace-modal, umb-modal-dialog, uui-dialog, uui-modal");
      for (const o of t) {
        const r = this.queryInsideDeep(o, "umb-body-layout");
        let e = !1;
        for (const s of r) {
          const i = this.resolveAliasFromContext(o, s);
          if (this.injectIntoUmbBodyLayoutShadow(s, i)) {
            console.log("✅ KV: logo header inserted via umb-body-layout SR", { alias: i, layout: s }), e = !0;
            break;
          }
          this.addSlottedHeader(s, i), console.log("✅ KV: logo header inserted via slot on umb-body-layout", { alias: i, layout: s }), e = !0;
          break;
        }
        e || this.addHeaderToAnyDialog(void 0);
      }
    } catch {
    }
  }
  detectWorkspaceEditorsAndInject() {
    const t = this.querySelectorAllDeep("umb-workspace-editor");
    for (const o of t) {
      const r = o.shadowRoot;
      if (!r) continue;
      const e = r.querySelector("umb-body-layout"), s = this.processHeadline(o.getAttribute("headline") || o.textContent || "") || null;
      if (e) {
        if (this.injectIntoUmbBodyLayoutShadow(e, s)) {
          console.log("✅ KV: inserted logo inside umb-workspace-editor > umb-body-layout (SR)", { alias: s, ws: o, body: e });
          continue;
        }
        this.addSlottedHeader(e, s), console.log("✅ KV: inserted logo via slot inside umb-workspace-editor > umb-body-layout", { alias: s, ws: o, body: e });
      }
    }
  }
  resolveAliasFromContext(t, o) {
    const r = this.queryInsideDeep(t, "umb-workspace-editor");
    for (const e of r) {
      const s = e.getAttribute("headline") || "", i = this.processHeadline(s || e.textContent || "");
      if (i) return i;
    }
    if (o) {
      const e = o.closest("umb-workspace-editor");
      if (e) {
        const s = this.processHeadline(e.getAttribute("headline") || e.textContent || "");
        if (s) return s;
      }
    }
    return null;
  }
  processHeadline(t) {
    if (!t) return null;
    const o = t.toLowerCase().replace(/add |edit |single |block |element /g, "").trim().replace(/\s+/g, "");
    return o && o.length > 2 ? o : null;
  }
  injectIntoUmbBodyLayoutShadow(t, o) {
    const r = t.shadowRoot;
    if (!r) return !1;
    let e = r.querySelector("#header");
    if (e) {
      if (e.querySelector(".kv-modal-logo-header")) return !0;
      const s = this.buildHeader(o);
      s.classList.add("kv-modal-logo-header");
      try {
        e.style.display = "block";
      } catch {
      }
      try {
        return e.insertBefore(s, e.firstChild), !0;
      } catch {
      }
    }
    if (e = r.querySelector("#main"), e) {
      if (e.querySelector(":scope > .kv-modal-logo-header")) return !0;
      const s = this.buildHeader(o);
      s.classList.add("kv-modal-logo-header");
      try {
        return e.insertBefore(s, e.firstChild), !0;
      } catch {
      }
    }
    if (e = r.querySelector('[part="main"], uui-scroll-container, .uui-scroll-container'), e) {
      if (e.querySelector(":scope > .kv-modal-logo-header")) return !0;
      const s = this.buildHeader(o);
      s.classList.add("kv-modal-logo-header");
      try {
        return e.insertBefore(s, e.firstChild), !0;
      } catch {
      }
    }
    return !1;
  }
  addSlottedHeader(t, o) {
    if (t.querySelector(".kv-modal-logo-header")) return;
    const r = this.buildHeader(o);
    r.classList.add("kv-modal-logo-header"), r.setAttribute("slot", "header");
    try {
      t.appendChild(r);
    } catch {
    }
  }
  addHeaderToAnyDialog(t) {
    const o = this.querySelectorAllDeep("uui-dialog-layout, .uui-dialog-layout, uui-dialog, .dialog-container");
    for (const r of o) {
      if (r.querySelector(".kv-modal-logo-header")) continue;
      const e = this.buildHeader(t);
      e.classList.add("kv-modal-logo-header");
      try {
        r.firstChild ? r.insertBefore(e, r.firstChild) : r.appendChild(e);
      } catch {
      }
    }
  }
  buildHeader(t) {
    const o = document.createElement("div");
    o.className = "block-workspace-thumbnail-header";
    const r = document.createElement("div");
    r.className = "block-workspace-thumbnail-container";
    const e = document.createElement("img");
    e.className = "block-workspace-thumbnail-image";
    let s = null;
    return t && (s = `/companentresim/${t}.png`), e.src = s || this.staticLogoUrlPrimary, e.alt = t ? `${t} Block Thumbnail` : "Block Logo", e.onerror = () => {
      if (t) {
        const i = `/companentresim/${t}`;
        e.onerror = () => {
          e.src = this.staticLogoUrlFallback, e.onerror = () => {
            e.remove();
            const a = document.createElement("div");
            a.textContent = "Logo", a.style.cssText = "width:140px;height:60px;border:1px dashed #ccc;border-radius:6px;display:flex;align-items:center;justify-content:center;background:#fff;color:#666;font:12px/1.2 sans-serif;", r.appendChild(a);
          };
        }, e.src = i;
      } else
        e.onerror = () => {
          e.remove();
          const i = document.createElement("div");
          i.textContent = "Logo", i.style.cssText = "width:140px;height:60px;border:1px dashed #ccc;border-radius:6px;display:flex;align-items:center;justify-content:center;background:#fff;color:#666;font:12px/1.2 sans-serif;", r.appendChild(i);
        }, e.src = this.staticLogoUrlFallback;
    }, r.appendChild(e), o.appendChild(r), o;
  }
  addGlobalStyles() {
    if (document.head.querySelector("#kv-block-modal-logo-style")) return;
    const t = document.createElement("style");
    t.id = "kv-block-modal-logo-style", t.textContent = `
      .block-workspace-thumbnail-header { width: 100%; padding: 12px 16px; border-bottom: 1px solid var(--uui-color-border, #d8dee4); background: linear-gradient(135deg, #f8f9fa 0%, #e9ecef 100%); display: flex; justify-content: center; align-items: center; order: -1000; margin-bottom: 8px; position: sticky; top: 0; z-index: 10000; box-sizing: border-box; }
      .block-workspace-thumbnail-container { display: flex; justify-content: center; align-items: center; max-width: 180px; border-radius: 8px; background: white; padding: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.15); }
      .block-workspace-thumbnail-image { max-width: 100%; max-height: 70px; height: auto; object-fit: contain; border-radius: 6px; border: 1px solid #dee2e6; }
      umb-body-layout[header-fit-height] { display: flex; flex-direction: column; }
      umb-body-layout[header-fit-height] .block-workspace-thumbnail-header { order: -1000; flex-shrink: 0; width: 100%; }
    `, document.head.appendChild(t);
  }
  render() {
    return u`<style>:host{display:none}</style>`;
  }
};
l.styles = [h`:host{display:none}`];
l = f([
  m("umb-block-workspace-view-edit-extend")
], l);
const g = l;
export {
  l as UmbBlockWorkspaceViewEditExtension,
  g as default
};
//# sourceMappingURL=UmbBlockWorkspaceViewEditExtension-D_2k-L1X.js.map
