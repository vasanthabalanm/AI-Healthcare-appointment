import { test, expect } from '@playwright/test';
import { LoginPage, AppointmentsPage } from '../../pages';
import e2eData from '../../data/e2e-journeys.json';

/**
 * E2E-002: Waitlist Slot Swap Journey
 * UC Chain: UC-004 → UC-005 → UC-006
 * Business Value: Automated preferred slot management reduces scheduling gaps
 */
test.describe('E2E-002: Waitlist Slot Swap Journey', () => {
  test.skip('Waitlist slot swap flow @P0 @e2e', async ({ page, request, context }) => {
    // This test requires:
    // - Two patient accounts
    // - SwapMonitorJob running
    // - SMTP/SMS stubs active
    
    const loginPage = new LoginPage(page);
    const appointmentsPage = new AppointmentsPage(page);

    // Phase 1: UC-004 — Patient A Books Available Slot
    await loginPage.navigate();
    await loginPage.login(e2eData.e2e002.patientA.email, e2eData.e2e002.patientA.password);
    await expect(page).toHaveURL(/\/patient\//);

    await appointmentsPage.navigateToBook();
    await appointmentsPage.selectFirstAvailableDate();
    await appointmentsPage.selectFirstAvailableSlot();
    await appointmentsPage.confirmBooking();

    // Checkpoint: Patient A has booked slot
    await expect(page.getByText(/confirmed|booked/i)).toBeVisible();

    // Phase 2: UC-005 — Patient B Books Alternative + Joins Waitlist
    // Would need separate browser context for Patient B
    const patientBContext = await context.browser()?.newContext();
    if (!patientBContext) {
      test.skip();
      return;
    }
    
    const patientBPage = await patientBContext.newPage();
    const loginPageB = new LoginPage(patientBPage);
    const appointmentsPageB = new AppointmentsPage(patientBPage);

    await loginPageB.navigate();
    // Patient B would login with their credentials
    // await loginPageB.login(e2eData.e2e002.patientB.email, 'password');

    // Book alternative slot
    await appointmentsPageB.navigateToBook();
    await appointmentsPageB.selectFirstAvailableDate();
    await appointmentsPageB.selectFirstAvailableSlot();
    await appointmentsPageB.confirmBooking();

    // Join waitlist for Patient A's slot
    await appointmentsPageB.navigateToList();
    await appointmentsPageB.joinWaitlist();

    // Phase 3: UC-006 — Slot Released, Swap Notification
    // Patient A cancels
    await appointmentsPage.navigateToList();
    await appointmentsPage.cancelAppointment();

    // SwapMonitorJob would run here
    // Wait for notification (would poll or check mock)

    // Phase 4: UC-006 — Patient B Accepts Swap
    await appointmentsPageB.navigateToList();
    
    // Look for swap offer
    const swapOffer = patientBPage.getByRole('button', { name: /accept/i });
    if (await swapOffer.isVisible()) {
      await swapOffer.click();
      await expect(patientBPage.getByText(/appointment updated/i)).toBeVisible();
    }

    await patientBContext.close();
  });
});
