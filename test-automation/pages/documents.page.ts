import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Documents page object
 */
export class DocumentsPage extends BasePage {
  readonly path = '/patient/documents';

  constructor(page: Page) {
    super(page);
  }

  get uploadButton(): Locator {
    return this.page.getByRole('button', { name: /upload/i });
  }

  get uploadButtonFallback(): Locator {
    return this.page.getByTestId('btn-upload-doc');
  }

  get fileInput(): Locator {
    return this.page.locator('input[type="file"]');
  }

  get fileInputFallback(): Locator {
    return this.page.getByTestId('file-input');
  }

  get uploadConfirmButton(): Locator {
    return this.page.getByRole('button', { name: /upload|confirm/i });
  }

  get documentList(): Locator {
    return this.page.getByTestId('document-list');
  }

  get documentRows(): Locator {
    return this.page.getByRole('row').filter({ hasText: /.pdf|.docx|.png|.jpg/i });
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async openUploadModal(): Promise<void> {
    await this.uploadButton.click();
  }

  async selectFile(filePath: string): Promise<void> {
    await this.fileInput.setInputFiles(filePath);
  }

  async confirmUpload(): Promise<void> {
    await this.uploadConfirmButton.click();
  }

  async uploadDocument(filePath: string): Promise<void> {
    await this.openUploadModal();
    await this.selectFile(filePath);
    await this.confirmUpload();
  }
}
