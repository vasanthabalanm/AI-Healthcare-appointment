import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Staff Schedule page object
 */
export class SchedulePage extends BasePage {
  readonly path = '/staff/schedule';

  constructor(page: Page) {
    super(page);
  }

  get scheduleRows(): Locator {
    return this.page.getByRole('row');
  }

  get scheduledAppointments(): Locator {
    return this.page.getByRole('row').filter({ hasText: /scheduled/i });
  }

  get arrivedAppointments(): Locator {
    return this.page.getByRole('row').filter({ hasText: /arrived/i });
  }

  get checkInButton(): Locator {
    return this.page.getByRole('button', { name: /check in/i });
  }

  get checkInButtonFallback(): Locator {
    return this.page.getByTestId('btn-checkin');
  }

  get highRiskFlag(): Locator {
    return this.page.locator('[data-risk="high"], .risk-high, [class*="risk-flag"]');
  }

  get outreachNoteInput(): Locator {
    return this.page.getByLabel(/note|outreach/i);
  }

  get saveNoteButton(): Locator {
    return this.page.getByRole('button', { name: /save/i });
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async checkInPatient(patientNameOrRow: string | Locator): Promise<void> {
    let row: Locator;
    if (typeof patientNameOrRow === 'string') {
      row = this.page.getByRole('row', { name: new RegExp(patientNameOrRow, 'i') });
    } else {
      row = patientNameOrRow;
    }
    await row.getByRole('button', { name: /check in/i }).click();
  }

  getAppointmentRow(patientName: string): Locator {
    return this.page.getByRole('row', { name: new RegExp(patientName, 'i') });
  }

  getAppointmentStatusBadge(row: Locator): Locator {
    return row.locator('[class*="badge"], [class*="status"]');
  }

  async addOutreachNote(note: string): Promise<void> {
    await this.outreachNoteInput.fill(note);
    await this.saveNoteButton.click();
  }
}
