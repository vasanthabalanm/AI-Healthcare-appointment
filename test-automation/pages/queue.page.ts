import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Staff Queue page object
 */
export class QueuePage extends BasePage {
  readonly path = '/staff/queue';

  constructor(page: Page) {
    super(page);
  }

  get walkInButton(): Locator {
    return this.page.getByRole('button', { name: /walk-in|register/i });
  }

  get walkInButtonFallback(): Locator {
    return this.page.getByTestId('btn-walkin');
  }

  get patientSearchBox(): Locator {
    return this.page.getByRole('searchbox');
  }

  get addToQueueButton(): Locator {
    return this.page.getByRole('button', { name: /add to queue/i });
  }

  get createNewPatientButton(): Locator {
    return this.page.getByRole('button', { name: /create new/i });
  }

  get queueRows(): Locator {
    return this.page.getByRole('row');
  }

  get queueRowFallback(): Locator {
    return this.page.getByTestId('queue-row');
  }

  get removeButton(): Locator {
    return this.page.getByRole('button', { name: /remove/i });
  }

  get estimatedWaitText(): Locator {
    return this.page.getByText(/estimated wait/i);
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async openWalkInForm(): Promise<void> {
    await this.walkInButton.click();
  }

  async searchPatient(name: string): Promise<void> {
    await this.patientSearchBox.fill(name);
  }

  async selectPatientResult(name: string): Promise<void> {
    await this.page.getByRole('option', { name: new RegExp(name, 'i') }).click();
  }

  async addToQueue(): Promise<void> {
    await this.addToQueueButton.click();
  }

  async registerWalkIn(patientName: string): Promise<void> {
    await this.openWalkInForm();
    await this.searchPatient(patientName);
    await this.selectPatientResult(patientName);
    await this.addToQueue();
  }

  async createNewWalkIn(data: { name: string; dob: string; phone: string }): Promise<void> {
    await this.openWalkInForm();
    await this.searchPatient(data.name);
    await this.createNewPatientButton.click();
    await this.page.getByLabel('Name').fill(data.name);
    await this.page.getByLabel('Date of Birth').fill(data.dob);
    await this.page.getByLabel('Phone').fill(data.phone);
    await this.addToQueue();
  }

  getQueueRowByPosition(position: number): Locator {
    return this.queueRows.nth(position);
  }

  async removeFromQueue(position: number): Promise<void> {
    const row = this.getQueueRowByPosition(position);
    await row.getByRole('button', { name: /remove/i }).click();
  }
}
