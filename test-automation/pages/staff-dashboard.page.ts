import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Staff Dashboard page object
 */
export class StaffDashboardPage extends BasePage {
  readonly path = '/staff/dashboard';

  constructor(page: Page) {
    super(page);
  }

  get navigation(): Locator {
    return this.page.getByRole('navigation');
  }

  get navigationFallback(): Locator {
    return this.page.getByTestId('nav-staff');
  }

  get scheduleLink(): Locator {
    return this.page.getByRole('link', { name: /schedule/i });
  }

  get queueLink(): Locator {
    return this.page.getByRole('link', { name: /queue/i });
  }

  get patientsLink(): Locator {
    return this.page.getByRole('link', { name: /patients/i });
  }

  get todaySchedule(): Locator {
    return this.page.getByTestId('today-schedule');
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async goToSchedule(): Promise<void> {
    await this.scheduleLink.click();
  }

  async goToQueue(): Promise<void> {
    await this.queueLink.click();
  }

  async goToPatients(): Promise<void> {
    await this.patientsLink.click();
  }
}
