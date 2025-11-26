import { LitElement, css, html } from 'lit';
import { customElement } from 'lit/decorators.js';

@customElement('kv-workspace-modal-logo-extend')
export class KvWorkspaceModalLogoWrapper extends LitElement {
  private _injected = false;
  private staticLogoUrlPrimary = '/companentresim/logo.png';
  private staticLogoUrlFallback = '/companentresim/singleVideo.png';

  connectedCallback(): void {
    super.connectedCallback();
    const original = (window as any).kvWorkspaceModalOriginal as { elementName: string; load: () => Promise<any> } | undefined;
    if (original?.load) {
      original.load().then(() => {
        this.requestUpdate();
        setTimeout(() => this.tryInject(), 150);
      });
    }
  }

  private tryInject() {
    if (this._injected) return;
    const layouts = this.querySelectorAllDeep('umb-body-layout', (this.renderRoot as unknown as Element) || this as unknown as Element);
    for (const layout of layouts) {
      if (this.injectIntoUmbBodyLayoutShadow(layout)) {
        this._injected = true;
        break;
      }
    }
    if (!this._injected) setTimeout(() => this.tryInject(), 300);
  }

  private querySelectorAllDeep(selector: string, root?: Element | ShadowRoot): Element[] {
    const result: Element[] = [];
    const queue: (Element | ShadowRoot)[] = [];
    const start: Element | ShadowRoot | null = root ?? (this.shadowRoot ?? this as unknown as Element);
    if (start) queue.push(start);
    const visited = new Set<Node>();
    while (queue.length) {
      const scope = queue.shift()!;
      if (visited.has(scope)) continue;
      visited.add(scope);
      if ('querySelectorAll' in scope) {
        try { (scope as any).querySelectorAll(selector).forEach((el: Element) => result.push(el)); } catch {}
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

  private injectIntoUmbBodyLayoutShadow(layout: Element): boolean {
    const sr = (layout as any).shadowRoot as ShadowRoot | null;
    if (!sr) return false;

    let container: HTMLElement | null = sr.querySelector('#header');
    if (container) {
      if (!container.querySelector('.kv-modal-logo-header')) {
        const header = this.buildHeader();
        header.classList.add('kv-modal-logo-header');
        try { container.style.display = 'block'; } catch {}
        try { container.insertBefore(header, container.firstChild); return true; } catch {}
      } else return true;
    }

    container = sr.querySelector('#main');
    if (container) {
      if (!container.querySelector(':scope > .kv-modal-logo-header')) {
        const header = this.buildHeader();
        header.classList.add('kv-modal-logo-header');
        try { container.insertBefore(header, container.firstChild); return true; } catch {}
      } else return true;
    }

    container = sr.querySelector('[part="main"], uui-scroll-container, .uui-scroll-container');
    if (container) {
      if (!container.querySelector(':scope > .kv-modal-logo-header')) {
        const header = this.buildHeader();
        header.classList.add('kv-modal-logo-header');
        try { container.insertBefore(header, container.firstChild); return true; } catch {}
      } else return true;
    }

    return false;
  }

  private buildHeader(): HTMLDivElement {
    const header = document.createElement('div');
    header.className = 'block-workspace-thumbnail-header';
    const box = document.createElement('div');
    box.className = 'block-workspace-thumbnail-container';
    const img = document.createElement('img');
    img.className = 'block-workspace-thumbnail-image';
    img.alt = 'Block Logo';
    img.src = this.staticLogoUrlPrimary;
    img.onerror = () => {
      img.onerror = () => {
        img.remove();
        const ph = document.createElement('div');
        ph.textContent = 'Logo';
        ph.style.cssText = 'width:140px;height:60px;border:1px dashed #ccc;border-radius:6px;display:flex;align-items:center;justify-content:center;background:#fff;color:#666;font:12px/1.2 sans-serif;';
        box.appendChild(ph);
      };
      img.src = this.staticLogoUrlFallback;
    };
    box.appendChild(img);
    header.appendChild(box);
    return header;
  }

  render() {
    const original = (window as any).kvWorkspaceModalOriginal as { elementName: string } | undefined;
    const tag = original?.elementName || 'div';
    return html`<${tag}></${tag}>`;
  }

  static styles = css`
    :host{display:block}
    .block-workspace-thumbnail-header{width:100%;padding:12px 16px;border-bottom:1px solid var(--uui-color-border,#d8dee4);background:linear-gradient(135deg,#f8f9fa 0%,#e9ecef 100%);display:flex;justify-content:center;align-items:center;margin-bottom:8px;position:sticky;top:0;z-index:10000;box-sizing:border-box}
    .block-workspace-thumbnail-container{display:flex;justify-content:center;align-items:center;max-width:180px;border-radius:8px;background:#fff;padding:8px;box-shadow:0 2px 8px rgba(0,0,0,.15)}
    .block-workspace-thumbnail-image{max-width:100%;max-height:70px;height:auto;object-fit:contain;border-radius:6px;border:1px solid #dee2e6}
  `;
}

export default KvWorkspaceModalLogoWrapper;

declare global {
  interface HTMLElementTagNameMap {
    'kv-workspace-modal-logo-extend': KvWorkspaceModalLogoWrapper;
  }
}
