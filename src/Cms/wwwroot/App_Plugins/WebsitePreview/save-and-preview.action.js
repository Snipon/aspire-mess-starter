import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { UmbWorkspaceActionBase } from '@umbraco-cms/backoffice/workspace';

// "Save and preview" for a headless site: saves the document and opens the draft on the website's staff preview
// (URL provider "website", Headless/WebsitePreviewUrlProvider.cs) instead of Umbraco's template-based preview, which
// this CMS cannot render. Replaces Umb.WorkspaceAction.Document.SaveAndPreview (and with it Umbraco's preview options).
export class HeadlessSaveAndPreviewAction extends UmbWorkspaceActionBase {
  async execute() {
    const workspace = await this.getContext(UMB_DOCUMENT_WORKSPACE_CONTEXT);
    if (!workspace) throw new Error('The document workspace context is missing');
    await workspace.saveAndPreview('website');
  }
}

export { HeadlessSaveAndPreviewAction as api };
