import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Medical Coding Verification page object
 */
export class CodingPage extends BasePage {
  constructor(page: Page) {
    super(page);
  }

  getPath(patientId: string | number): string {
    return `/staff/coding?patientId=${patientId}`;
  }

  get suggestionRows(): Locator {
    return this.page.getByRole('row').filter({ has: this.page.locator('[class*="suggestion"]') });
  }

  get pendingSuggestions(): Locator {
    return this.page.getByRole('row').filter({ hasText: /pending/i });
  }

  get acceptedSuggestions(): Locator {
    return this.page.getByRole('row').filter({ hasText: /accepted/i });
  }

  get modifiedSuggestions(): Locator {
    return this.page.getByRole('row').filter({ hasText: /modified/i });
  }

  get rejectedSuggestions(): Locator {
    return this.page.getByRole('row').filter({ hasText: /rejected/i });
  }

  get acceptButton(): Locator {
    return this.page.getByRole('button', { name: /accept/i });
  }

  get acceptButtonFallback(): Locator {
    return this.page.getByTestId('btn-accept-code');
  }

  get editButton(): Locator {
    return this.page.getByRole('button', { name: /edit|modify/i });
  }

  get rejectButton(): Locator {
    return this.page.getByRole('button', { name: /reject/i });
  }

  get saveButton(): Locator {
    return this.page.getByRole('button', { name: /save|accept/i });
  }

  get codeInput(): Locator {
    return this.page.getByLabel(/code/i);
  }

  async navigate(patientId: string | number): Promise<void> {
    await this.goto(this.getPath(patientId));
  }

  getSuggestionRow(index: number): Locator {
    return this.pendingSuggestions.nth(index);
  }

  async acceptSuggestion(rowIndex: number): Promise<void> {
    const row = this.getSuggestionRow(rowIndex);
    await row.getByRole('button', { name: /accept/i }).click();
  }

  async rejectSuggestion(rowIndex: number): Promise<void> {
    const row = this.getSuggestionRow(rowIndex);
    await row.getByRole('button', { name: /reject/i }).click();
  }

  async modifySuggestion(rowIndex: number, newCode: string): Promise<void> {
    const row = this.getSuggestionRow(rowIndex);
    await row.getByRole('button', { name: /edit|modify/i }).click();
    await this.codeInput.clear();
    await this.codeInput.fill(newCode);
    await this.saveButton.click();
  }
}
