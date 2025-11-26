import { UmbBlockCatalogueModalData, UmbBlockCatalogueModalElement } from "@umbraco-cms/backoffice/block";
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from "@umbraco-cms/backoffice/document";
import { UMB_VARIANT_WORKSPACE_CONTEXT } from "@umbraco-cms/backoffice/workspace";
import { customElement } from "lit/decorators.js";
import { css, html } from "lit";
import { BlockfilterClient, OpenAPI } from "../blockfilter-api";
import { UMB_MODAL_CONTEXT } from "@umbraco-cms/backoffice/modal";

@customElement('umb-block-catalogue-modal-extend')
export class UmbBlockCatalogueModalElementExtension extends UmbBlockCatalogueModalElement
{
  #alias = '';
  #unique = '';
  #pageType = '';

  constructor() {
    super();

    console.log('🚀 Modal açıldı - Block Filter Extension başlatılıyor');

    // grab a few vals we need for the model
    this.observe(this._manager?.propertyAlias, (value) => {
      this.#alias = value ?? '';
    });
    this.consumeContext(UMB_VARIANT_WORKSPACE_CONTEXT, (workspaceContext) => {
          this.#unique = workspaceContext?.getUnique() ?? '';      
        });

    this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (workspaceContext) => {
        this.observe(workspaceContext?.contentTypeUnique, (value) => {
          this.#pageType = value ?? '';
        });
    });

    // modal provides us with data we forward to api
    this.consumeContext(UMB_MODAL_CONTEXT, (modalContext) => {
      console.log('🎯 Modal context alındı');
      if(modalContext?.data) {
        this.handleBlocks(modalContext.data);
      }
    });
  }

  async handleBlocks(data: any) {
    console.log('=== HANDLE BLOCKS CALLED ===');
    console.log('Data received:', data);
    console.log('Current DOM state when handleBlocks called:');
    console.log('- This element parent:', this.parentElement);
    console.log('- Document modals:', document.querySelectorAll('umb-modal, umb-modal-layout, uui-modal'));
    
    const bfc = new BlockfilterClient({
      TOKEN: OpenAPI.TOKEN,
      BASE: OpenAPI.BASE
    });

    const requestObject = {
          ...data as any, pageId: this.#unique, editingAlias: this.#alias, pageTypeId: this.#pageType
    } 

    const response = await bfc.v1.postApiV1BlockfilterRemodel({
      requestBody: requestObject
    });

    const oldFilter = this.data?.clipboardFilter;

    // Umbraco'nun orijinal veri yapısını koruyarak sadece blokları filtrele
    this.data = response as unknown as UmbBlockCatalogueModalData;

    console.log('📊 Response alındı, render metodu ile header gösterilecek');

    this.data.clipboardFilter = async (clipboardEntryDetail) => {
        // Allowed keys from this.data.blocks
        const allowedKeys = this.data?.blocks.map(b => b.contentElementTypeKey.toLowerCase());

        // Gather all contentTypeKeys from clipboard entry
        const clipboardKeys = clipboardEntryDetail.values
            .flatMap(v => v.value?.contentData || [])
            .map(cd => cd.contentTypeKey?.toLowerCase())
            .filter(Boolean);

        // Check: all clipboard contentTypeKeys must be in allowedKeys
        const allAllowed = clipboardKeys.every(key => allowedKeys?.includes(key));

        if (!allAllowed) {
            return false;
        }

        // Keep old filter behavior if present
        if (typeof oldFilter === 'function') {
            return await oldFilter(clipboardEntryDetail);
        }
        return true;
    };

    // hacky as heck but this will trigger the line
    // this.#itemManager.setUniques(this.data.blocks.map((block) => block.contentElementTypeKey));
    // which we need to set the available blocks in the modal since everything else we could manipulate is private.
    this.connectedCallback();
  }



  connectedCallback() {
    super.connectedCallback();
    console.log('🔌 Component bağlandı');
  }





  addBlockThumbnails() {
    if (!this.data?.blocks) return;

    // Create thumbnail CSS for block items
    const styleElement = document.createElement('style');
    let cssRules = '';

    this.data.blocks.forEach(block => {
      if (block.thumbnail && block.contentElementTypeKey) {
        const key = block.contentElementTypeKey.toLowerCase();
        cssRules += `
          [data-element-type-key="${key}"] uui-card-content::before {
            content: '';
            display: block;
            width: 100%;
            height: 100px;
            background-image: url('${block.thumbnail}');
            background-size: contain;
            background-position: center;
            background-repeat: no-repeat;
            border-radius: 4px;
            margin-bottom: 8px;
            border: 1px solid #e0e0e0;
          }
          
          [data-element-type-key="${key}"] uui-card {
            min-height: 150px !important;
          }
        `;
      }
    });

    styleElement.textContent = cssRules;
    styleElement.id = 'block-thumbnails-style';
    document.head.appendChild(styleElement);
  }

  render() {
    console.log('🎨 RENDER metodu çağrıldı!');
    console.log('Component data:', this.data);
    
    const originalContent = super.render();
    
    return html`
      <div class="block-filter-custom-header">
        <div class="header-container">
          <img 
            src="/companentresim/header-logo.png" 
            alt="Block Filter Header" 
            class="header-logo"
            @load=${() => console.log('✅ Header logo render ile yüklendi!')}
            @error=${() => console.log('❌ Header logo render ile yüklenemedi!')}
          />
        </div>
      </div>
      ${originalContent}
    `;
  }

  static styles = [
    ...UmbBlockCatalogueModalElement.styles || [],
    css`
      .block-filter-custom-header {
        padding: 16px;
        border-bottom: 1px solid #e0e0e0;
        background: #fff;
        margin-bottom: 16px;
        position: sticky;
        top: 0;
        z-index: 1000;
      }

      .header-container {
        display: flex;
        justify-content: center;
        align-items: center;
      }

      .header-logo {
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
}

export default UmbBlockCatalogueModalElementExtension;

declare global {
  interface HTMLElementTagNameMap {
    'umb-block-catalogue-modal-extend': UmbBlockCatalogueModalElementExtension;
  }
}
