import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Appointments page object (patient booking)
 */
export class AppointmentsPage extends BasePage {
  readonly listPath = '/patient/appointments';
  readonly bookPath = '/patient/book';

  constructor(page: Page) {
    super(page);
  }

  get appointmentRows(): Locator {
    return this.page.getByRole('row');
  }

  get scheduledAppointments(): Locator {
    return this.page.getByRole('row', { name: /scheduled/i });
  }

  get confirmBookingButton(): Locator {
    return this.page.getByRole('button', { name: 'Confirm booking' });
  }

  get confirmBookingButtonFallback(): Locator {
    return this.page.getByTestId('btn-confirm-booking');
  }

  get cancelButton(): Locator {
    return this.page.getByRole('button', { name: /cancel/i });
  }

  get cancelButtonFallback(): Locator {
    return this.page.getByTestId('btn-cancel-appt');
  }

  get rescheduleButton(): Locator {
    return this.page.getByRole('button', { name: /reschedule/i });
  }

  get waitlistButton(): Locator {
    return this.page.getByRole('button', { name: /waitlist|preferred/i });
  }

  get joinWaitlistButton(): Locator {
    return this.page.getByRole('button', { name: /join waitlist/i });
  }

  get confirmButton(): Locator {
    return this.page.getByRole('button', { name: /confirm/i });
  }

  get summaryPanel(): Locator {
    return this.page.locator('.booking-summary');
  }

  get availableDateCells(): Locator {
    return this.page.locator('.cal-day--available');
  }

  get availableSlotButtons(): Locator {
    return this.page.locator('.slot-btn');
  }

  get nextMonthButton(): Locator {
    return this.page.getByRole('button', { name: 'Next month' });
  }

  async navigateToList(): Promise<void> {
    await this.goto(this.listPath);
  }

  async navigateToBook(): Promise<void> {
    await this.goto(this.bookPath);
  }

  async selectFirstAvailableDate(): Promise<void> {
    // Wait for calendar to load
    await this.page.waitForSelector('.cal-grid', { timeout: 10000 });
    
    // Try current month first
    let availableDate = this.availableDateCells.first();
    
    // If no available dates in current month, go to next month
    if (!(await availableDate.isVisible({ timeout: 2000 }).catch(() => false))) {
      await this.nextMonthButton.click();
      await this.page.waitForTimeout(500); // Wait for calendar to update
      availableDate = this.availableDateCells.first();
    }
    
    await availableDate.click();
  }

  async selectFirstAvailableSlot(): Promise<void> {
    // Wait for slots to load after selecting date
    await this.page.waitForSelector('.slot-list', { timeout: 10000 });
    await this.availableSlotButtons.first().click();
  }

  async confirmBooking(): Promise<void> {
    await this.confirmBookingButton.click();
  }

  async cancelAppointment(): Promise<void> {
    await this.cancelButton.click();
    await this.confirmButton.click();
  }

  async joinWaitlist(): Promise<void> {
    await this.waitlistButton.click();
  }

  async acceptSwap(): Promise<void> {
    await this.page.getByRole('button', { name: /accept/i }).click();
  }
}
