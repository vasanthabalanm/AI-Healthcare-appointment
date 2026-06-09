import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Patient 360° View page object
 */
export class Patient360Page extends BasePage {
  constructor(page: Page) {
    super(page);
  }

  getPath(patientId: string | number): string {
    return `/staff/patients/${patientId}/view360`;
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: /360.*patient.*view/i });
  }

  get vitalsSection(): Locator {
    return this.page.getByTestId('vitals-section');
  }

  get medicalHistorySection(): Locator {
    return this.page.getByTestId('medical-history-section');
  }

  get medicationsSection(): Locator {
    return this.page.getByTestId('medications-section');
  }

  get allergiesSection(): Locator {
    return this.page.getByTestId('allergies-section');
  }

  get verificationStatusBadge(): Locator {
    return this.page.locator('[class*="verification"], [data-status]');
  }

  get verifyButton(): Locator {
    return this.page.getByRole('button', { name: /verify|mark verified/i });
  }

  get verifyButtonFallback(): Locator {
    return this.page.getByTestId('btn-verify-360');
  }

  get generateCodesButton(): Locator {
    return this.page.getByRole('button', { name: /generate codes/i });
  }

  get generateCodesButtonFallback(): Locator {
    return this.page.getByTestId('btn-generate-codes');
  }

  get conflictFlags(): Locator {
    return this.page.getByRole('alert', { name: /conflict/i });
  }

  get conflictFlagsFallback(): Locator {
    return this.page.getByTestId('conflict-flag');
  }

  async navigate(patientId: string | number): Promise<void> {
    await this.goto(this.getPath(patientId));
  }

  async verifyPatient(): Promise<void> {
    await this.verifyButton.click();
  }

  async generateCodes(): Promise<void> {
    await this.generateCodesButton.click();
  }
}
