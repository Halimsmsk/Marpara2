import { LitElement } from "lit";
import { customElement } from "lit/decorators.js";
import { css, html } from "lit";

@customElement('umb-block-workspace-view-edit-extend')
export class UmbBlockWorkspaceViewEditExtension extends LitElement {
  private initialized = false;
  private observers: MutationObserver[] = [];
  private periodicCheckerId: number | undefined;
  private modalPollerId: number | undefined;

  // Static logo path (fallback)
  private staticLogoUrlPrimary = '/companentresim/logo.png';
  private staticLogoUrlFallback = '/companentresim/singleVideo.png';

  private seenModals = new WeakSet<Element>();
  private observedHosts = new WeakSet<Node>();

  constructor() {
    super();
    console.log('🚀 UmbBlockWorkspaceViewEditExtension: Constructor called');
  }

  connectedCallback() {
    super.connectedCallback();
    if (!this.initialized) {
      this.initialize();
      this.initialized = true;
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this.observers.forEach(o => o.disconnect());
    this.observers = [];
    if (this.periodicCheckerId) window.clearInterval(this.periodicCheckerId);
    if (this.modalPollerId) window.clearInterval(this.modalPollerId);
  }

  private initialize() {
    this.addGlobalStyles();

    setTimeout(() => this.detectAndInject(), 100);
    setTimeout(() => this.detectAndInject(), 500);
    setTimeout(() => this.detectAndInject(), 1000);
    setTimeout(() => this.detectAndInject(), 2000);

    this.modalPollerId = window.setInterval(() => this.pollAndLogModals(), 800);

    const mo = new MutationObserver((_muts) => {
      // Changes in light DOM – schedule a scan
      setTimeout(() => { this.detectAndInject(); this.detectWorkspaceEditorsAndInject(); }, 50);
    });
    mo.observe(document.body, { childList: true, subtree: true });
    this.observers.push(mo);

    // Observe modal containers and their shadow roots explicitly (Umbraco 16 places content inside Shadow DOM)
    this.attachDeepObservers();
    // Re-attach periodically in case hosts are recreated
    this.periodicCheckerId = window.setInterval(() => { this.attachDeepObservers(); this.detectAndInject(); this.detectWorkspaceEditorsAndInject(); }, 3000);
  }

  // Attach MutationObservers to modal hosts AND their shadowRoots so Shadow DOM mutations are caught
  private attachDeepObservers() {
    const hosts = this.querySelectorAllDeep('umb-backoffice-modal-container, uui-modal-container, uui-modal-sidebar, umb-workspace-modal, uui-dialog, uui-modal, umb-workspace-editor');
    for (const host of hosts) {
      this.observeNode(host);
      const sr = (host as any).shadowRoot as ShadowRoot | null;
      if (sr) this.observeNode(sr);
    }
  }

  private observeNode(node: Node) {
    if (this.observedHosts.has(node)) return;
    this.observedHosts.add(node);

    const obs = new MutationObserver((_muts) => {
      // When anything changes under these hosts, try inject quickly
      setTimeout(() => { this.detectWorkspaceEditorsAndInject(); this.detectAndInject(); }, 30);
    });
    try {
      obs.observe(node as Element, { childList: true, subtree: true });
      this.observers.push(obs);
      console.log('👁️ KV: observing', node instanceof Element ? node.tagName.toLowerCase() : '[shadow-root]', node);
    } catch {}

    // If node contains elements with shadowRoot, observe them too
    if ((node as any).querySelectorAll) {
      try {
        const els = (node as any).querySelectorAll('*');
        els.forEach((el: any) => { if (el?.shadowRoot) this.observeNode(el.shadowRoot as ShadowRoot); });
      } catch {}
    }
  }

  private querySelectorAllDeep(selector: string, root: Document | ShadowRoot | Element = document): Element[] {
    const result: Element[] = [];
    const queue: (Element | ShadowRoot)[] = [];
    const start: Element | ShadowRoot | null = root instanceof Document ? (root.documentElement as Element | null) : (root as Element | ShadowRoot);
    if (start) queue.push(start);
    const visited = new Set<Node>();
    while (queue.length) {
      const scope = queue.shift()!;
      if (visited.has(scope)) continue;
      visited.add(scope);
      if ('querySelectorAll' in scope) {
        try {
          (scope as Element | ShadowRoot).querySelectorAll(selector).forEach(el => result.push(el));
        } catch {}
      }
      const children = scope instanceof ShadowRoot ? Array.from(scope.children) : Array.from((scope as Element).children);
      for (const c of children) {
        queue.push(c as Element);
        if ((c as Element).shadowRoot) queue.push((c as Element).shadowRoot!);
      }
      if ((scope as Element).shadowRoot) queue.push((scope as Element).shadowRoot!);
    }
    return Array.from(new Set(result));
  }

  private queryInsideDeep(root: Element, selector: string): Element[] {
    const result: Element[] = [];
    const queue: (Element | ShadowRoot)[] = [];
    if (root.shadowRoot) queue.push(root.shadowRoot);
    queue.push(root);
    const visited = new Set<Node>();
    while (queue.length) {
      const scope = queue.shift()!;
      if (visited.has(scope)) continue;
      visited.add(scope);
      if ('querySelectorAll' in scope) {
        try { (scope as Element | ShadowRoot).querySelectorAll(selector).forEach(el => result.push(el)); } catch {}
      }
      const children = scope instanceof ShadowRoot ? scope.children : (scope as Element).children;
      Array.from(children).forEach(c => {
        queue.push(c as Element);
        if ((c as Element).shadowRoot) queue.push((c as Element).shadowRoot!);
      });
      if ((scope as Element).shadowRoot) queue.push((scope as Element).shadowRoot!);
    }
    return Array.from(new Set(result));
  }

  private pollAndLogModals() {
    const modalSelectors = [
      'umb-backoffice-modal-container',
      'uui-modal-container',
      'uui-modal-sidebar',
      'umb-workspace-modal',
      'umb-modal-dialog',
      'uui-dialog',
      'uui-modal',
      'umb-workspace-editor'
    ].join(',');

    const modals = this.querySelectorAllDeep(modalSelectors);
    if (modals.length === 0) return;

    for (const modal of modals) {
      if (!this.seenModals.has(modal)) {
        this.seenModals.add(modal);
        console.log('🟢 Tespit:', modal.tagName.toLowerCase(), modal);
      }
    }
  }

  private detectAndInject() {
    try {
      const modals = this.querySelectorAllDeep('umb-backoffice-modal-container, uui-modal-container, uui-modal-sidebar, umb-workspace-modal, umb-modal-dialog, uui-dialog, uui-modal');

      for (const modal of modals) {
        const layouts = this.queryInsideDeep(modal as Element, 'umb-body-layout');
        let injected = false;
        for (const layout of layouts) {
          const alias = this.resolveAliasFromContext(modal as Element, layout);
          if (this.injectIntoUmbBodyLayoutShadow(layout, alias)) {
            console.log('✅ KV: logo header inserted via umb-body-layout SR', { alias, layout });
            injected = true;
            break;
          }
          this.addSlottedHeader(layout, alias);
          console.log('✅ KV: logo header inserted via slot on umb-body-layout', { alias, layout });
          injected = true;
          break;
        }
        if (!injected) this.addHeaderToAnyDialog(undefined);
      }
    } catch {
      // ignore
    }
  }

  private detectWorkspaceEditorsAndInject() {
    const editors = this.querySelectorAllDeep('umb-workspace-editor');
    for (const ws of editors) {
      const sr = (ws as any).shadowRoot as ShadowRoot | null;
      if (!sr) continue;
      const body = sr.querySelector('umb-body-layout') as Element | null;
      const alias = this.processHeadline((ws as HTMLElement).getAttribute('headline') || (ws as HTMLElement).textContent || '') || null;
      if (body) {
        if (this.injectIntoUmbBodyLayoutShadow(body, alias)) {
          console.log('✅ KV: inserted logo inside umb-workspace-editor > umb-body-layout (SR)', { alias, ws, body });
          continue;
        }
        this.addSlottedHeader(body, alias);
        console.log('✅ KV: inserted logo via slot inside umb-workspace-editor > umb-body-layout', { alias, ws, body });
      }
    }
  }

  private resolveAliasFromContext(modal: Element, layout?: Element): string | null {
    const editors = this.queryInsideDeep(modal, 'umb-workspace-editor');
    for (const ed of editors) {
      const headlineAttr = (ed as HTMLElement).getAttribute('headline') || '';
      const alias = this.processHeadline(headlineAttr || ed.textContent || '');
      if (alias) return alias;
    }
    if (layout) {
      const ws = layout.closest('umb-workspace-editor') as HTMLElement | null;
      if (ws) {
        const alias = this.processHeadline(ws.getAttribute('headline') || ws.textContent || '');
        if (alias) return alias;
      }
    }
    return null;
  }

  private processHeadline(text: string): string | null {
    if (!text) return null;
    const processed = text.toLowerCase().replace(/add |edit |single |block |element /g, '').trim().replace(/\s+/g, '');
    return processed && processed.length > 2 ? processed : null;
  }

  private injectIntoUmbBodyLayoutShadow(layout: Element, alias?: string | null): boolean {
    const sr = (layout as any).shadowRoot as ShadowRoot | null;
    if (!sr) return false;

    let container: HTMLElement | null = sr.querySelector('#header') as HTMLElement | null;
    if (container) {
      if (container.querySelector('.kv-modal-logo-header')) return true;
      const header = this.buildHeader(alias);
      header.classList.add('kv-modal-logo-header');
      try { container.style.display = 'block'; } catch {}
      try { container.insertBefore(header, container.firstChild); return true; } catch {}
    }

    container = sr.querySelector('#main') as HTMLElement | null;
    if (container) {
      if (container.querySelector(':scope > .kv-modal-logo-header')) return true;
      const header = this.buildHeader(alias);
      header.classList.add('kv-modal-logo-header');
      try { container.insertBefore(header, container.firstChild); return true; } catch {}
    }

    container = sr.querySelector('[part="main"], uui-scroll-container, .uui-scroll-container') as HTMLElement | null;
    if (container) {
      if (container.querySelector(':scope > .kv-modal-logo-header')) return true;
      const header = this.buildHeader(alias);
      header.classList.add('kv-modal-logo-header');
      try { container.insertBefore(header, container.firstChild); return true; } catch {}
    }

    return false;
  }

  private addSlottedHeader(layout: Element, alias?: string | null) {
    if ((layout as Element).querySelector('.kv-modal-logo-header')) return;
    const header = this.buildHeader(alias);
    header.classList.add('kv-modal-logo-header');
    header.setAttribute('slot', 'header');
    try { layout.appendChild(header); } catch {}
  }

  private addHeaderToAnyDialog(alias?: string | null) {
    const containers = this.querySelectorAllDeep('uui-dialog-layout, .uui-dialog-layout, uui-dialog, .dialog-container');
    for (const el of containers) {
      if ((el as Element).querySelector('.kv-modal-logo-header')) continue;
      const header = this.buildHeader(alias);
      header.classList.add('kv-modal-logo-header');
      try {
        if (el.firstChild) (el as Element).insertBefore(header, el.firstChild);
        else (el as Element).appendChild(header);
      } catch {}
    }
  }

  private buildHeader(alias?: string | null): HTMLDivElement {
    const header = document.createElement('div');
    header.className = 'block-workspace-thumbnail-header';
    const box = document.createElement('div');
    box.className = 'block-workspace-thumbnail-container';
    const img = document.createElement('img');
    img.className = 'block-workspace-thumbnail-image';

    let src: string | null = null;
    if (alias) src = `/companentresim/${alias}.png`;
    img.src = src || this.staticLogoUrlPrimary;
    img.alt = alias ? `${alias} Block Thumbnail` : 'Block Logo';

    img.onerror = () => {
      if (alias) {
        const alt = `/companentresim/${alias}`;
        img.onerror = () => {
          img.src = this.staticLogoUrlFallback;
          img.onerror = () => {
            img.remove();
            const ph = document.createElement('div');
            ph.textContent = 'Logo';
            ph.style.cssText = 'width:140px;height:60px;border:1px dashed #ccc;border-radius:6px;display:flex;align-items:center;justify-content:center;background:#fff;color:#666;font:12px/1.2 sans-serif;';
            box.appendChild(ph);
          };
        };
        img.src = alt;
      } else {
        img.onerror = () => {
          img.remove();
          const ph = document.createElement('div');
          ph.textContent = 'Logo';
          ph.style.cssText = 'width:140px;height:60px;border:1px dashed #ccc;border-radius:6px;display:flex;align-items:center;justify-content:center;background:#fff;color:#666;font:12px/1.2 sans-serif;';
          box.appendChild(ph);
        };
        img.src = this.staticLogoUrlFallback;
      }
    };

    box.appendChild(img);
    header.appendChild(box);
    return header;
  }

  private addGlobalStyles() {
    if (document.head.querySelector('#kv-block-modal-logo-style')) return;
    const s = document.createElement('style');
    s.id = 'kv-block-modal-logo-style';
    s.textContent = `
      .block-workspace-thumbnail-header { width: 100%; padding: 12px 16px; border-bottom: 1px solid var(--uui-color-border, #d8dee4); background: linear-gradient(135deg, #f8f9fa 0%, #e9ecef 100%); display: flex; justify-content: center; align-items: center; order: -1000; margin-bottom: 8px; position: sticky; top: 0; z-index: 10000; box-sizing: border-box; }
      .block-workspace-thumbnail-container { display: flex; justify-content: center; align-items: center; max-width: 180px; border-radius: 8px; background: white; padding: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.15); }
      .block-workspace-thumbnail-image { max-width: 100%; max-height: 70px; height: auto; object-fit: contain; border-radius: 6px; border: 1px solid #dee2e6; }
      umb-body-layout[header-fit-height] { display: flex; flex-direction: column; }
      umb-body-layout[header-fit-height] .block-workspace-thumbnail-header { order: -1000; flex-shrink: 0; width: 100%; }
    `;
    document.head.appendChild(s);
  }

  render() {
    return html`<style>:host{display:none}</style>`;
  }

  static styles = [css`:host{display:none}`];
}

export default UmbBlockWorkspaceViewEditExtension;

declare global {
  interface HTMLElementTagNameMap {
    'umb-block-workspace-view-edit-extend': UmbBlockWorkspaceViewEditExtension;
  }
}