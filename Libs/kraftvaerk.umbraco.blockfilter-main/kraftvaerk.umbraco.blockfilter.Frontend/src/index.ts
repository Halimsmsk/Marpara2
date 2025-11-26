import { ManifestBase, UmbEntryPointOnInit } from '@umbraco-cms/backoffice/extension-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { ManifestModal } from '@umbraco-cms/backoffice/modal';
import { OpenAPI } from './blockfilter-api/index.ts';

console.log('?? KV index.ts loading at:', new Date().toISOString());

const modalAlias = 'Umb.Modal.BlockCatalogue';
const workspaceViewEditAlias = 'Umb.Block.WorkspaceViewEdit.Thumbnail';

interface WorkspaceExtensionManifest extends ManifestBase {
  type: 'element';
  elementName: string;
  js: () => Promise<any>;
  weight?: number;
}

const manifests: Array<ManifestModal | WorkspaceExtensionManifest> = [
  {
    type: 'modal',
    alias: modalAlias,
    name: 'Block Catalogue Modal Extension',
    elementName: 'umb-block-catalogue-modal-extend',
    js: () => import('./elements/UmbBlockCatalogueModalElementExtension.ts'),
    weight: -10000,
  },
  {
    type: 'element',
    alias: workspaceViewEditAlias,
    name: 'Block Workspace View Edit Thumbnail Extension',
    elementName: 'umb-block-workspace-view-edit-extend',
    js: () => import('./elements/UmbBlockWorkspaceViewEditExtension.ts'),
    weight: -9999,
  }
];

function removeAndRegister(extensionRegistry: any) {
  console.log('?? removeAndRegister called');
  
  setTimeout(() => {
    const blockCatalogueExtension = extensionRegistry.getByAlias(modalAlias);
    console.log('?? Block catalogue extension check:', blockCatalogueExtension);

    if (!blockCatalogueExtension) {
      console.log('?? Block catalogue extension not found, retrying...');
      removeAndRegister(extensionRegistry);
    } else {
      console.log('?? Unregistering existing extensions...');
      extensionRegistry.unregister(blockCatalogueExtension.alias);
      
      const workspaceExtension = extensionRegistry.getByAlias(workspaceViewEditAlias);
      if (workspaceExtension) {
        console.log('?? Found existing workspace extension, unregistering...');
        extensionRegistry.unregister(workspaceExtension.alias);
      }
      
      console.log('?? Registering our manifests...');
      extensionRegistry.registerMany(manifests);
      console.log('? Extensions registered successfully');
    }
  }, 200);
}

export const onInit: UmbEntryPointOnInit = async (_host, extensionRegistry) => {
  console.log('?? KV onInit called at:', new Date().toISOString());

  _host.consumeContext(UMB_AUTH_CONTEXT, async (authContext) => {
    console.log('?? KV AUTH_CONTEXT consumed:', authContext);

    const token = await authContext?.getLatestToken() ?? '';
    const base = authContext?.getServerUrl() ?? '';

    OpenAPI.BASE = base;
    OpenAPI.TOKEN = token;
    
    console.log('?? Kraftvaerk Block Filter Extensions initialized');
    
    removeAndRegister(extensionRegistry);

    // Simple test: load workspace extension directly
    setTimeout(async () => {
      try {
        console.log('?? Direct import test...');
        const wsModule = await import('./elements/UmbBlockWorkspaceViewEditExtension.ts');
        console.log('?? Workspace module loaded:', wsModule);
        
        // Create test element
        const testEl = document.createElement('umb-block-workspace-view-edit-extend');
        testEl.style.display = 'none';
        document.body.appendChild(testEl);
        console.log('?? Test element created');
        
      } catch (err) {
        console.error('?? Direct import failed:', err);
      }
    }, 1000);
    
    // Simple global click logger
    document.addEventListener('click', (evt) => {
      const target = evt.target as Element;
      if (target.textContent?.includes('Add') || target.className?.includes('add')) {
        console.log('?? Add-related click:', target.tagName, target.textContent?.substring(0, 30));
      }
    }, { capture: true });
  });
};

console.log('?? KV index.ts loaded at:', new Date().toISOString());