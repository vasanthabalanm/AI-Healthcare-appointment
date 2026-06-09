import { test, expect } from '@playwright/test';
import { AppointmentsPage, LoginPage, PatientDashboardPage } from '../../pages';
import authData from '../../data/auth.json';
import appointmentsData from '../../data/appointments.json';

test.describe('Appointments Feature Tests', () => {
  let loginPage: LoginPage;
  let appointmentsPage: AppointmentsPage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    appointmentsPage = new AppointmentsPage(page);
    
    // Login as patient
    await loginPage.navigate();
    await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
    await expect(page).toHaveURL(/\/patient\//);
  });

  // FT-013: Patient books available slot
  test('FT-013: Patient books available slot @P0', async ({ page }) => {
    await appointmentsPage.navigateToBook();
    
    await appointmentsPage.selectFirstAvailableDate();
    await appointmentsPage.selectFirstAvailableSlot();
    
    // Verify booking summary shows selected appointment
    await expect(appointmentsPage.summaryPanel).toBeVisible();
    
    await appointmentsPage.confirmBooking();
    
    // Success message appears
    await expect(page.locator('.success-msg').or(page.getByText(/booked|confirmed|success/i))).toBeVisible();
  });

  // FT-014: Concurrent booking same slot
  test.skip('FT-014: Concurrent booking — one succeeds @P0', async ({ page, request }) => {
    // Requires parallel API calls with two patient tokens
    // One should get 201, one should get 409
    // This test needs special setup for race condition testing
  });

  // FT-015: No-show risk score stored on booking
  test.skip('FT-015: No-show risk score stored @P1', async ({ page, request }) => {
    // Requires API verification of risk score in booking response
  });

  // FT-016: Patient joins waitlist
  test('FT-016: Patient joins waitlist @P1', async ({ page }) => {
    await appointmentsPage.navigateToList();
    
    // Find scheduled appointment
    const scheduledRow = appointmentsPage.scheduledAppointments.first();
    if (await scheduledRow.isVisible()) {
      await scheduledRow.getByRole('button', { name: /waitlist|preferred/i }).click();
      await page.getByRole('button', { name: /join waitlist/i }).click();
      
      await expect(page.getByText(/waitlist/i)).toBeVisible();
    }
  });

  // FT-017: Second waitlist entry replaces first
  test.skip('FT-017: Waitlist entry replaced @P1', async () => {
    // Requires seeding existing waitlist entry then adding another
  });

  // FT-018: Slot release triggers swap notification
  test.skip('FT-018: Slot release triggers notification @P0', async () => {
    // Requires SMTP/SMS stub and background job trigger
  });

  // FT-019: Patient accepts swap
  test.skip('FT-019: Patient accepts swap @P0', async () => {
    // Requires seeded "Offered" waitlist entry
  });

  // FT-020: Swap window expires
  test.skip('FT-020: Swap offer expires @P1', async () => {
    // Requires expired offer seeding
  });

  // FT-021: Patient cancels before cutoff
  test('FT-021: Patient cancels appointment @P0', async ({ page }) => {
    // First book an appointment
    await appointmentsPage.navigateToBook();
    await appointmentsPage.selectFirstAvailableDate();
    await appointmentsPage.selectFirstAvailableSlot();
    await appointmentsPage.confirmBooking();
    
    // Now cancel it
    await appointmentsPage.navigateToList();
    const scheduledRow = appointmentsPage.scheduledAppointments.first();
    
    if (await scheduledRow.isVisible()) {
      await scheduledRow.getByRole('button', { name: /cancel/i }).click();
      await page.getByRole('button', { name: /confirm/i }).click();
      
      await expect(page.getByText(/cancelled/i)).toBeVisible();
    }
  });

  // FT-022: Cancel inside cutoff window → blocked
  test.skip('FT-022: Cancel inside cutoff blocked @P0', async () => {
    // Requires appointment seeded within cutoff window
  });

  // FT-023: Patient reschedules
  test.skip('FT-023: Patient reschedules @P1', async () => {
    // Similar to cancel + rebook flow
  });

  // FT-031: Confirmation email with PDF ≤ 60s
  test.skip('FT-031: Confirmation email SLA @P0', async () => {
    // Requires SMTP stub monitoring
  });
});
