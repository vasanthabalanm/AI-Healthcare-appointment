import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Patient Dashboard page object
 */
export class PatientDashboardPage extends BasePage {
  readonly path = '/patient/dashboard';

  constructor(page: Page) {
    super(page);
  }

  get heading(): Locator {
    // Dashboard heading is "Welcome back, {firstName}"
    return this.page.getByRole('heading', { name: /welcome back/i, level: 1 });
  }

  get appointmentsLink(): Locator {
    return this.page.getByRole('link', { name: /appointments/i });
  }

  get documentsLink(): Locator {
    return this.page.getByRole('link', { name: /documents/i });
  }

  get profileLink(): Locator {
    return this.page.getByRole('link', { name: /profile/i });
  }

  get upcomingAppointments(): Locator {
    return this.page.getByTestId('upcoming-appointments');
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async goToAppointments(): Promise<void> {
    await this.appointmentsLink.click();
  }

  async goToDocuments(): Promise<void> {
    await this.documentsLink.click();
  }

  async goToProfile(): Promise<void> {
    await this.profileLink.click();
  }
}
