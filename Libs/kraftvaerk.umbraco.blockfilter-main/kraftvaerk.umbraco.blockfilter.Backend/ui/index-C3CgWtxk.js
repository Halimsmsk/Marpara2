import { UMB_AUTH_CONTEXT as k } from "@umbraco-cms/backoffice/auth";
const a = {
  BASE: "",
  VERSION: "1.0",
  WITH_CREDENTIALS: !1,
  CREDENTIALS: "include",
  TOKEN: void 0,
  USERNAME: void 0,
  PASSWORD: void 0,
  HEADERS: void 0,
  ENCODE_PATH: void 0
};
console.log("?? KV index.ts loading at:", (/* @__PURE__ */ new Date()).toISOString());
const d = "Umb.Modal.BlockCatalogue", r = "Umb.Block.WorkspaceViewEdit.Thumbnail", u = [
  {
    type: "modal",
    alias: d,
    name: "Block Catalogue Modal Extension",
    elementName: "umb-block-catalogue-modal-extend",
    js: () => import("./UmbBlockCatalogueModalElementExtension-CHJwsFlC.js"),
    weight: -1e4
  },
  {
    type: "element",
    alias: r,
    name: "Block Workspace View Edit Thumbnail Extension",
    elementName: "umb-block-workspace-view-edit-extend",
    js: () => import("./UmbBlockWorkspaceViewEditExtension-D_2k-L1X.js"),
    weight: -9999
  }
];
function g(o) {
  console.log("?? removeAndRegister called"), setTimeout(() => {
    const l = o.getByAlias(d);
    if (console.log("?? Block catalogue extension check:", l), !l)
      console.log("?? Block catalogue extension not found, retrying..."), g(o);
    else {
      console.log("?? Unregistering existing extensions..."), o.unregister(l.alias);
      const e = o.getByAlias(r);
      e && (console.log("?? Found existing workspace extension, unregistering..."), o.unregister(e.alias)), console.log("?? Registering our manifests..."), o.registerMany(u), console.log("? Extensions registered successfully");
    }
  }, 200);
}
const A = async (o, l) => {
  console.log("?? KV onInit called at:", (/* @__PURE__ */ new Date()).toISOString()), o.consumeContext(k, async (e) => {
    console.log("?? KV AUTH_CONTEXT consumed:", e);
    const m = await (e == null ? void 0 : e.getLatestToken()) ?? "", E = (e == null ? void 0 : e.getServerUrl()) ?? "";
    a.BASE = E, a.TOKEN = m, console.log("?? Kraftvaerk Block Filter Extensions initialized"), g(l), setTimeout(async () => {
      try {
        console.log("?? Direct import test...");
        const s = await import("./UmbBlockWorkspaceViewEditExtension-D_2k-L1X.js");
        console.log("?? Workspace module loaded:", s);
        const n = document.createElement("umb-block-workspace-view-edit-extend");
        n.style.display = "none", document.body.appendChild(n), console.log("?? Test element created");
      } catch (s) {
        console.error("?? Direct import failed:", s);
      }
    }, 1e3), document.addEventListener("click", (s) => {
      var t, i, c;
      const n = s.target;
      ((t = n.textContent) != null && t.includes("Add") || (i = n.className) != null && i.includes("add")) && console.log("?? Add-related click:", n.tagName, (c = n.textContent) == null ? void 0 : c.substring(0, 30));
    }, { capture: !0 });
  });
};
console.log("?? KV index.ts loaded at:", (/* @__PURE__ */ new Date()).toISOString());
export {
  a as O,
  A as o
};
//# sourceMappingURL=index-C3CgWtxk.js.map
