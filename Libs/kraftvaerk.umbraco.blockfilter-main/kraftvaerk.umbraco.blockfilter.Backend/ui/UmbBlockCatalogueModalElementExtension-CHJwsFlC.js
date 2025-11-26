var $ = (t) => {
  throw TypeError(t);
};
var j = (t, e, o) => e.has(t) || $("Cannot " + o);
var i = (t, e, o) => (j(t, e, "read from private field"), o ? o.call(t) : e.get(t)), f = (t, e, o) => e.has(t) ? $("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, o), h = (t, e, o, r) => (j(t, e, "write to private field"), r ? r.call(t, o) : e.set(t, o), o);
import { UmbBlockCatalogueModalElement as I } from "@umbraco-cms/backoffice/block";
import { UMB_DOCUMENT_WORKSPACE_CONTEXT as F } from "@umbraco-cms/backoffice/document";
import { UMB_VARIANT_WORKSPACE_CONTEXT as K } from "@umbraco-cms/backoffice/workspace";
import { i as W, t as z } from "./lit-element-FqBfIOu4.js";
import { O as M } from "./index-C3CgWtxk.js";
import { UMB_MODAL_CONTEXT as V } from "@umbraco-cms/backoffice/modal";
class J {
  constructor(e) {
    this.config = e;
  }
}
class _ extends Error {
  constructor(e, o, r) {
    super(r), this.name = "ApiError", this.url = o.url, this.status = o.status, this.statusText = o.statusText, this.body = o.body, this.request = e;
  }
}
class G extends Error {
  constructor(e) {
    super(e), this.name = "CancelError";
  }
  get isCancelled() {
    return !0;
  }
}
var p, y, b, g, S, x, k;
class X {
  constructor(e) {
    f(this, p);
    f(this, y);
    f(this, b);
    f(this, g);
    f(this, S);
    f(this, x);
    f(this, k);
    h(this, p, !1), h(this, y, !1), h(this, b, !1), h(this, g, []), h(this, S, new Promise((o, r) => {
      h(this, x, o), h(this, k, r);
      const a = (l) => {
        i(this, p) || i(this, y) || i(this, b) || (h(this, p, !0), i(this, x) && i(this, x).call(this, l));
      }, s = (l) => {
        i(this, p) || i(this, y) || i(this, b) || (h(this, y, !0), i(this, k) && i(this, k).call(this, l));
      }, n = (l) => {
        i(this, p) || i(this, y) || i(this, b) || i(this, g).push(l);
      };
      return Object.defineProperty(n, "isResolved", {
        get: () => i(this, p)
      }), Object.defineProperty(n, "isRejected", {
        get: () => i(this, y)
      }), Object.defineProperty(n, "isCancelled", {
        get: () => i(this, b)
      }), e(a, s, n);
    }));
  }
  get [Symbol.toStringTag]() {
    return "Cancellable Promise";
  }
  then(e, o) {
    return i(this, S).then(e, o);
  }
  catch(e) {
    return i(this, S).catch(e);
  }
  finally(e) {
    return i(this, S).finally(e);
  }
  cancel() {
    if (!(i(this, p) || i(this, y) || i(this, b))) {
      if (h(this, b, !0), i(this, g).length)
        try {
          for (const e of i(this, g))
            e();
        } catch (e) {
          console.warn("Cancellation threw an error", e);
          return;
        }
      i(this, g).length = 0, i(this, k) && i(this, k).call(this, new G("Request aborted"));
    }
  }
  get isCancelled() {
    return i(this, b);
  }
}
p = new WeakMap(), y = new WeakMap(), b = new WeakMap(), g = new WeakMap(), S = new WeakMap(), x = new WeakMap(), k = new WeakMap();
const B = (t) => t != null, T = (t) => typeof t == "string", O = (t) => T(t) && t !== "", H = (t) => typeof t == "object" && typeof t.type == "string" && typeof t.stream == "function" && typeof t.arrayBuffer == "function" && typeof t.constructor == "function" && typeof t.constructor.name == "string" && /^(Blob|File)$/.test(t.constructor.name) && /^(Blob|File)$/.test(t[Symbol.toStringTag]), L = (t) => t instanceof FormData, Q = (t) => {
  try {
    return btoa(t);
  } catch {
    return Buffer.from(t).toString("base64");
  }
}, Y = (t) => {
  const e = [], o = (a, s) => {
    e.push(`${encodeURIComponent(a)}=${encodeURIComponent(String(s))}`);
  }, r = (a, s) => {
    B(s) && (Array.isArray(s) ? s.forEach((n) => {
      r(a, n);
    }) : typeof s == "object" ? Object.entries(s).forEach(([n, l]) => {
      r(`${a}[${n}]`, l);
    }) : o(a, s));
  };
  return Object.entries(t).forEach(([a, s]) => {
    r(a, s);
  }), e.length > 0 ? `?${e.join("&")}` : "";
}, Z = (t, e) => {
  const o = t.ENCODE_PATH || encodeURI, r = e.url.replace("{api-version}", t.VERSION).replace(/{(.*?)}/g, (s, n) => {
    var l;
    return (l = e.path) != null && l.hasOwnProperty(n) ? o(String(e.path[n])) : s;
  }), a = `${t.BASE}${r}`;
  return e.query ? `${a}${Y(e.query)}` : a;
}, ee = (t) => {
  if (t.formData) {
    const e = new FormData(), o = (r, a) => {
      T(a) || H(a) ? e.append(r, a) : e.append(r, JSON.stringify(a));
    };
    return Object.entries(t.formData).filter(([r, a]) => B(a)).forEach(([r, a]) => {
      Array.isArray(a) ? a.forEach((s) => o(r, s)) : o(r, a);
    }), e;
  }
}, C = async (t, e) => typeof e == "function" ? e(t) : e, te = async (t, e) => {
  const [o, r, a, s] = await Promise.all([
    C(e, t.TOKEN),
    C(e, t.USERNAME),
    C(e, t.PASSWORD),
    C(e, t.HEADERS)
  ]), n = Object.entries({
    Accept: "application/json",
    ...s,
    ...e.headers
  }).filter(([l, m]) => B(m)).reduce((l, [m, d]) => ({
    ...l,
    [m]: String(d)
  }), {});
  if (O(o) && (n.Authorization = `Bearer ${o}`), O(r) && O(a)) {
    const l = Q(`${r}:${a}`);
    n.Authorization = `Basic ${l}`;
  }
  return e.body !== void 0 && (e.mediaType ? n["Content-Type"] = e.mediaType : H(e.body) ? n["Content-Type"] = e.body.type || "application/octet-stream" : T(e.body) ? n["Content-Type"] = "text/plain" : L(e.body) || (n["Content-Type"] = "application/json")), new Headers(n);
}, oe = (t) => {
  var e;
  if (t.body !== void 0)
    return (e = t.mediaType) != null && e.includes("/json") ? JSON.stringify(t.body) : T(t.body) || H(t.body) || L(t.body) ? t.body : JSON.stringify(t.body);
}, re = async (t, e, o, r, a, s, n) => {
  const l = new AbortController(), m = {
    headers: s,
    body: r ?? a,
    method: e.method,
    signal: l.signal
  };
  return t.WITH_CREDENTIALS && (m.credentials = t.CREDENTIALS), n(() => l.abort()), await fetch(o, m);
}, ae = (t, e) => {
  if (e) {
    const o = t.headers.get(e);
    if (T(o))
      return o;
  }
}, ne = async (t) => {
  if (t.status !== 204)
    try {
      const e = t.headers.get("Content-Type");
      if (e)
        return ["application/json", "application/problem+json"].some((a) => e.toLowerCase().startsWith(a)) ? await t.json() : await t.text();
    } catch (e) {
      console.error(e);
    }
}, se = (t, e) => {
  const r = {
    400: "Bad Request",
    401: "Unauthorized",
    403: "Forbidden",
    404: "Not Found",
    500: "Internal Server Error",
    502: "Bad Gateway",
    503: "Service Unavailable",
    ...t.errors
  }[e.status];
  if (r)
    throw new _(t, e, r);
  if (!e.ok) {
    const a = e.status ?? "unknown", s = e.statusText ?? "unknown", n = (() => {
      try {
        return JSON.stringify(e.body, null, 2);
      } catch {
        return;
      }
    })();
    throw new _(
      t,
      e,
      `Generic Error: status: ${a}; status text: ${s}; body: ${n}`
    );
  }
}, ie = (t, e) => new X(async (o, r, a) => {
  try {
    const s = Z(t, e), n = ee(e), l = oe(e), m = await te(t, e);
    if (!a.isCancelled) {
      const d = await re(t, e, s, l, n, m, a), E = await ne(d), c = ae(d, e.responseHeader), u = {
        url: s,
        ok: d.ok,
        status: d.status,
        statusText: d.statusText,
        body: c ?? E
      };
      se(e, u), o(u.body);
    }
  } catch (s) {
    r(s);
  }
});
class le extends J {
  constructor(e) {
    super(e);
  }
  /**
   * Request method
   * @param options The request options from the service
   * @returns CancelablePromise<T>
   * @throws ApiError
   */
  request(e) {
    return ie(this.config, e);
  }
}
class ce {
  constructor(e) {
    this.httpRequest = e;
  }
  /**
   * @returns any OK
   * @throws ApiError
   */
  postApiV1BlockfilterRemodel({
    requestBody: e
  }) {
    return this.httpRequest.request({
      method: "POST",
      url: "/api/v1/blockfilter/remodel",
      body: e,
      mediaType: "application/json",
      errors: {
        401: "The resource is protected and requires an authentication token"
      }
    });
  }
}
class de {
  constructor(e, o = le) {
    this.request = new o({
      BASE: (e == null ? void 0 : e.BASE) ?? "",
      VERSION: (e == null ? void 0 : e.VERSION) ?? "1.0",
      WITH_CREDENTIALS: (e == null ? void 0 : e.WITH_CREDENTIALS) ?? !1,
      CREDENTIALS: (e == null ? void 0 : e.CREDENTIALS) ?? "include",
      TOKEN: e == null ? void 0 : e.TOKEN,
      USERNAME: e == null ? void 0 : e.USERNAME,
      PASSWORD: e == null ? void 0 : e.PASSWORD,
      HEADERS: e == null ? void 0 : e.HEADERS,
      ENCODE_PATH: e == null ? void 0 : e.ENCODE_PATH
    }), this.v1 = new ce(this.request);
  }
}
var ue = Object.defineProperty, me = Object.getOwnPropertyDescriptor, U = (t) => {
  throw TypeError(t);
}, he = (t, e, o, r) => {
  for (var a = r > 1 ? void 0 : r ? me(e, o) : e, s = t.length - 1, n; s >= 0; s--)
    (n = t[s]) && (a = (r ? n(e, o, a) : n(a)) || a);
  return r && a && ue(e, o, a), a;
}, P = (t, e, o) => e.has(t) || U("Cannot " + o), q = (t, e, o) => (P(t, e, "read from private field"), e.get(t)), D = (t, e, o) => e.has(t) ? U("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, o), N = (t, e, o, r) => (P(t, e, "write to private field"), e.set(t, o), o), w, A, R;
let v = class extends I {
  constructor() {
    var t;
    super(), D(this, w, ""), D(this, A, ""), D(this, R, ""), console.log("🚀 Modal açıldı - Block Filter Extension başlatılıyor"), this.observe((t = this._manager) == null ? void 0 : t.propertyAlias, (e) => {
      N(this, w, e ?? "");
    }), this.consumeContext(K, (e) => {
      N(this, A, (e == null ? void 0 : e.getUnique()) ?? "");
    }), this.consumeContext(F, (e) => {
      this.observe(e == null ? void 0 : e.contentTypeUnique, (o) => {
        N(this, R, o ?? "");
      });
    }), this.consumeContext(V, (e) => {
      console.log("🎯 Modal context alındı, header eklemeye başlıyoruz"), e != null && e.data && this.handleBlocks(e.data);
    });
  }
  async handleBlocks(t) {
    var s;
    console.log("=== HANDLE BLOCKS CALLED ==="), console.log("Data received:", t), console.log("Current DOM state when handleBlocks called:"), console.log("- This element parent:", this.parentElement), console.log("- Document modals:", document.querySelectorAll("umb-modal, umb-modal-layout, uui-modal"));
    const e = new de({
      TOKEN: M.TOKEN,
      BASE: M.BASE
    }), o = {
      ...t,
      pageId: q(this, A),
      editingAlias: q(this, w),
      pageTypeId: q(this, R)
    }, r = await e.v1.postApiV1BlockfilterRemodel({
      requestBody: o
    }), a = (s = this.data) == null ? void 0 : s.clipboardFilter;
    this.data = r, console.log("Response received, trying to add header now..."), this.data.clipboardFilter = async (n) => {
      var E;
      const l = (E = this.data) == null ? void 0 : E.blocks.map((c) => c.contentElementTypeKey.toLowerCase());
      return n.values.flatMap((c) => {
        var u;
        return ((u = c.value) == null ? void 0 : u.contentData) || [];
      }).map((c) => {
        var u;
        return (u = c.contentTypeKey) == null ? void 0 : u.toLowerCase();
      }).filter(Boolean).every((c) => l == null ? void 0 : l.includes(c)) ? typeof a == "function" ? await a(n) : !0 : !1;
    }, this.connectedCallback(), setTimeout(() => this.addDirectHeader(), 100), setTimeout(() => this.addDirectHeader(), 500), setTimeout(() => this.addBlockThumbnails(), 100), setTimeout(() => this.addBlockThumbnails(), 500), setTimeout(() => this.addBlockThumbnails(), 1e3);
  }
  connectedCallback() {
    super.connectedCallback(), console.log("Block Filter Extension Connected"), console.log("This element:", this), console.log("Parent element:", this.parentElement), console.log("Shadow root:", this.shadowRoot), this.debugModalStructure(), this.injectHeaderCSS(), this.observeModalChanges(), this.tryDirectInsertion();
  }
  debugModalStructure() {
    var o;
    console.log("=== DEBUG MODAL STRUCTURE ==="), console.log("Component:", this.tagName), console.log("Component classes:", this.className), console.log("Component parent:", (o = this.parentElement) == null ? void 0 : o.tagName), [
      "umb-modal",
      "umb-modal-layout",
      "uui-modal",
      "uui-modal-container",
      '[data-element="modal"]',
      '[data-element="modal-layout"]',
      ".modal",
      ".modal-content"
    ].forEach((r) => {
      const a = document.querySelectorAll(r);
      a.length > 0 && console.log(`Found ${a.length} elements with selector "${r}":`, a);
    }), document.querySelectorAll("*").forEach((r) => {
      r.shadowRoot && console.log("Element with shadow root:", r.tagName, r);
    });
  }
  tryDirectInsertion() {
    console.log("🔍 Modal container'lara header eklemeye çalışıyorum..."), setTimeout(() => {
      const t = document.querySelector("umb-backoffice-modal-container");
      if (t && !t.querySelector(".block-filter-header-logo")) {
        console.log("✅ umb-backoffice-modal-container bulundu!");
        const e = document.createElement("div");
        e.className = "block-filter-header-logo", e.innerHTML = `
          <div style="
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 16px;
            border-bottom: 1px solid #e0e0e0;
            background: #fff;
            margin-bottom: 16px;
            position: relative;
            z-index: 1000;
          ">
            <img 
              src="/companentresim/header-logo.png" 
              alt="Component Header" 
              style="max-height: 60px; max-width: 200px; object-fit: contain;"
              onload="console.log('📸 Header logo başarıyla yüklendi!')"
              onerror="console.error('❌ Header logo yüklenemedi:', this.src)"
            />
          </div>
        `, t.firstChild ? t.insertBefore(e, t.firstChild) : t.appendChild(e), console.log("🎉 Header umb-backoffice-modal-container'a eklendi!");
      }
    }, 100), setTimeout(() => {
      const t = document.querySelector("uui-modal-sidebar");
      if (t && !t.querySelector(".block-filter-sidebar-header")) {
        console.log("✅ uui-modal-sidebar bulundu!");
        const e = document.createElement("div");
        e.className = "block-filter-sidebar-header", e.innerHTML = `
          <div style="
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 12px;
            border-bottom: 1px solid #e0e0e0;
            background: #f8f9fa;
            margin-bottom: 12px;
          ">
            <img 
              src="/companentresim/header-logo.png" 
              alt="Sidebar Header" 
              style="max-height: 40px; max-width: 150px; object-fit: contain;"
              onload="console.log('📸 Sidebar logo başarıyla yüklendi!')"
              onerror="console.error('❌ Sidebar logo yüklenemedi:', this.src)"
            />
          </div>
        `, t.firstChild ? t.insertBefore(e, t.firstChild) : t.appendChild(e), console.log("🎉 Header uui-modal-sidebar'a eklendi!");
      }
    }, 200), setTimeout(() => {
      this.insertIntoShadowRoots();
    }, 300);
  }
  insertIntoShadowRoots() {
    console.log("🔍 Shadow root'ları kontrol ediyorum..."), document.querySelectorAll("*").forEach((e) => {
      if (e.shadowRoot) {
        console.log("👻 Shadow root bulundu:", e.tagName);
        const o = e.shadowRoot.querySelector("umb-backoffice-modal-container") || e.shadowRoot.querySelector("uui-modal-sidebar") || e.shadowRoot.querySelector('[data-element="modal"]');
        if (o && !o.querySelector(".shadow-block-filter-header")) {
          console.log("✅ Shadow root içinde modal container bulundu!");
          const r = document.createElement("div");
          r.className = "shadow-block-filter-header", r.innerHTML = `
            <div style="
              display: flex;
              justify-content: center;
              align-items: center;
              padding: 16px;
              border-bottom: 1px solid #e0e0e0;
              background: #fff;
              margin-bottom: 16px;
            ">
              <img 
                src="/companentresim/header-logo.png" 
                alt="Shadow Header" 
                style="max-height: 60px; max-width: 200px; object-fit: contain;"
                onload="console.log('📸 Shadow header logo başarıyla yüklendi!')"
                onerror="console.error('❌ Shadow header logo yüklenemedi:', this.src)"
              />
            </div>
          `, o.firstChild ? o.insertBefore(r, o.firstChild) : o.appendChild(r), console.log("🎉 Header shadow root'a eklendi!");
        }
      }
    });
  }
  injectHeaderCSS() {
    const t = "umbraco-block-filter-header-style", e = document.head.querySelector(`#${t}`);
    e && e.remove();
    const o = document.createElement("style");
    o.id = t, o.textContent = `
      /* Umbraco 16 Modal Container Header CSS */
      umb-backoffice-modal-container::before {
        content: '';
        display: block;
        width: 100%;
        height: 80px;
        background-image: url('/companentresim/header-logo.png');
        background-size: contain;
        background-position: center;
        background-repeat: no-repeat;
        border-bottom: 1px solid #e0e0e0;
        margin-bottom: 16px;
        background-color: #fff;
        position: relative;
        z-index: 1000;
      }

      /* Modal Sidebar Header */
      uui-modal-sidebar::before {
        content: '';
        display: block;
        width: 100%;
        height: 60px;
        background-image: url('/companentresim/header-logo.png');
        background-size: contain;
        background-position: center;
        background-repeat: no-repeat;
        border-bottom: 1px solid #e0e0e0;
        margin-bottom: 12px;
        background-color: #f8f9fa;
      }

      /* Fallback selectors */
      .umb-backoffice-modal-container::before,
      [data-element="modal-container"]::before,
      .uui-modal-sidebar::before {
        content: '';
        display: block;
        width: 100%;
        height: 80px;
        background-image: url('/companentresim/header-logo.png');
        background-size: contain;
        background-position: center;
        background-repeat: no-repeat;
        border-bottom: 1px solid #e0e0e0;
        margin-bottom: 16px;
        background-color: #fff;
      }

      /* Ensure proper spacing */
      umb-backoffice-modal-container,
      uui-modal-sidebar {
        position: relative;
      }
    `, document.head.appendChild(o), console.log("💅 Modal container CSS enjekte edildi!");
  }
  observeModalChanges() {
    new MutationObserver((e) => {
      e.forEach((o) => {
        o.type === "childList" && o.addedNodes.forEach((r) => {
          if (r.nodeType === Node.ELEMENT_NODE) {
            const a = r;
            (a.tagName === "UMB-MODAL-LAYOUT" || a.querySelector("umb-modal-layout") || a.hasAttribute("data-element") && a.getAttribute("data-element") === "modal-layout") && setTimeout(() => this.addDirectHeader(a), 100);
          }
        });
      });
    }).observe(document.body, {
      childList: !0,
      subtree: !0
    }), setTimeout(() => this.addDirectHeader(), 100), setTimeout(() => this.addDirectHeader(), 500);
  }
  addDirectHeader(t) {
    const e = t || document.querySelector("umb-modal-layout") || document.querySelector('[data-element="modal-layout"]') || this.closest("umb-modal-layout");
    if (e && !e.querySelector(".block-filter-custom-header")) {
      const o = document.createElement("div");
      o.className = "block-filter-custom-header", o.innerHTML = `
        <div style="
          display: flex;
          justify-content: center;
          align-items: center;
          padding: 16px;
          border-bottom: 1px solid #e0e0e0;
          background: #fff;
          margin-bottom: 16px;
        ">
          <img 
            src="/companentresim/header-logo.png" 
            alt="Component Header" 
            style="max-height: 60px; max-width: 200px; object-fit: contain;"
            onload="console.log('Header logo loaded successfully')"
            onerror="console.warn('Header logo failed to load'); this.parentElement.parentElement.style.display='none';"
          />
        </div>
      `;
      const r = e.querySelector("uui-scroll-container") || e.querySelector(".uui-scroll-container") || e.firstElementChild || e;
      r && r.firstChild ? (r.insertBefore(o, r.firstChild), console.log("Direct header added to modal")) : r && (r.appendChild(o), console.log("Direct header appended to modal"));
    }
  }
  addBlockThumbnails() {
    var r;
    if (!((r = this.data) != null && r.blocks)) return;
    const t = document.head.querySelector("#block-thumbnails-style");
    t && t.remove();
    const e = document.createElement("style");
    let o = "";
    this.data.blocks.forEach((a) => {
      if (a.thumbnail && a.contentElementTypeKey) {
        const s = a.contentElementTypeKey.toLowerCase(), n = a.contentElementTypeKey.toUpperCase();
        o += `
          /* Standard block catalog item selectors */
          umb-block-catalog-item[data-element-type-key="${s}"] uui-card-content::before,
          umb-block-catalog-item[data-element-type-key="${n}"] uui-card-content::before,
          umb-block-catalog-item[element-type-key="${s}"] uui-card-content::before,
          umb-block-catalog-item[element-type-key="${n}"] uui-card-content::before,
          
          /* Alternative selectors for different DOM structures */
          [data-element-type-key="${s}"] uui-card-content::before,
          [data-element-type-key="${n}"] uui-card-content::before,
          [element-type-key="${s}"] uui-card-content::before,
          [element-type-key="${n}"] uui-card-content::before,
          
          /* Fallback selectors */
          umb-block-catalog-item[data-content-element-type-key="${s}"] uui-card-content::before,
          umb-block-catalog-item[data-content-element-type-key="${n}"] uui-card-content::before {
            content: '';
            display: block;
            width: 100%;
            height: 100px;
            background-image: url('${a.thumbnail}');
            background-size: contain;
            background-position: center;
            background-repeat: no-repeat;
            border-radius: 4px;
            margin-bottom: 8px;
            border: 1px solid #e0e0e0;
          }

          /* Ensure proper layout */
          umb-block-catalog-item[data-element-type-key="${s}"] uui-card,
          umb-block-catalog-item[data-element-type-key="${n}"] uui-card,
          [data-element-type-key="${s}"] uui-card,
          [data-element-type-key="${n}"] uui-card {
            min-height: 150px !important;
          }

          umb-block-catalog-item[data-element-type-key="${s}"] uui-card-content,
          umb-block-catalog-item[data-element-type-key="${n}"] uui-card-content,
          [data-element-type-key="${s}"] uui-card-content,
          [data-element-type-key="${n}"] uui-card-content {
            display: flex !important;
            flex-direction: column !important;
            align-items: center !important;
            text-align: center !important;
          }
        `;
      }
    }), e.textContent = o, e.id = "block-thumbnails-style", document.head.appendChild(e), this.directThumbnailInsertion();
  }
  directThumbnailInsertion() {
    var e;
    if (!((e = this.data) != null && e.blocks)) return;
    [this.shadowRoot, document].forEach((o) => {
      if (!o) return;
      [
        "umb-block-catalog-item",
        "[data-element-type-key]",
        "[element-type-key]",
        "[data-content-element-type-key]"
      ].forEach((a) => {
        o.querySelectorAll(a).forEach((n) => {
          var m;
          const l = n.getAttribute("data-element-type-key") || n.getAttribute("element-type-key") || n.getAttribute("data-content-element-type-key") || n.elementTypeKey;
          if (l) {
            const d = (m = this.data) == null ? void 0 : m.blocks.find(
              (E) => E.contentElementTypeKey.toLowerCase() === l.toLowerCase()
            );
            if (d != null && d.thumbnail && !n.querySelector(".custom-block-thumbnail")) {
              const c = document.createElement("img");
              c.src = d.thumbnail, c.className = "custom-block-thumbnail", c.style.cssText = `
                  width: 100%;
                  max-width: 120px;
                  height: 80px;
                  object-fit: contain;
                  border-radius: 4px;
                  margin-bottom: 8px;
                  border: 1px solid #e0e0e0;
                  display: block;
                `, c.onerror = () => {
                console.warn(`Failed to load thumbnail: ${d.thumbnail}`), c.style.display = "none";
              };
              const u = n.querySelector("uui-card-content") || n.querySelector(".card-content") || n.querySelector("uui-card") || n;
              u && u.firstChild && u.insertBefore(c, u.firstChild);
            }
          }
        });
      });
    });
  }
};
w = /* @__PURE__ */ new WeakMap();
A = /* @__PURE__ */ new WeakMap();
R = /* @__PURE__ */ new WeakMap();
v.styles = [
  ...I.styles || [],
  W`
      .custom-header {
        padding: 16px;
        border-bottom: 1px solid var(--uui-color-border);
        background: var(--uui-color-surface);
        margin-bottom: 16px;
      }

      .header-image-container {
        display: flex;
        justify-content: center;
        align-items: center;
      }

      .header-image {
        max-height: 60px;
        max-width: 200px;
        object-fit: contain;
      }

      /* Global styles for thumbnails */
      :host ::ng-deep .custom-block-thumbnail {
        width: 100% !important;
        max-width: 120px !important;
        height: 80px !important;
        object-fit: contain !important;
        border-radius: 4px !important;
        margin-bottom: 8px !important;
        border: 1px solid #e0e0e0 !important;
        display: block !important;
      }

      /* Ensure proper card layout */
      :host ::ng-deep umb-block-catalog-item uui-card {
        min-height: 150px !important;
      }

      :host ::ng-deep umb-block-catalog-item uui-card-content {
        display: flex !important;
        flex-direction: column !important;
        align-items: center !important;
        text-align: center !important;
      }
    `
];
v = he([
  z("umb-block-catalogue-modal-extend")
], v);
const Se = v;
export {
  v as UmbBlockCatalogueModalElementExtension,
  Se as default
};
//# sourceMappingURL=UmbBlockCatalogueModalElementExtension-CHJwsFlC.js.map
