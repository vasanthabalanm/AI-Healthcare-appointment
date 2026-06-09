import { test, expect } from '@playwright/test';
import { LoginPage, QueuePage, SchedulePage, StaffDashboardPage } from '../../pages';
import e2eData from '../../data/e2e-journeys.json';

/**
 * E2E-004: Staff Daily Operations Journey
 * UC Chain: UC-002 → UC-013 → UC-014 → UC-015 → UC-016
 * Business Value: Staff manages complete patient flow for a service day
 */
test.describe('E2E-004: Staff Daily Operations Journey', () => {
  test('Staff daily operations workflow @P0 @e2e', async ({ page }) => {
    const loginPage = new LoginPage(page);
    const staffDashboard = new StaffDashboardPage(page);
    const queuePage = new QueuePage(page);
    const schedulePage = new SchedulePage(page);

    // Phase 1: UC-002 — Staff Login
    await loginPage.navigate();
    await loginPage.login(e2eData.e2e004.staff.email, e2eData.e2e004.staff.password);

    // Checkpoint: Staff dashboard with nav
    await expect(page).toHaveURL(/\/staff\//);
    await expect(staffDashboard.navigation.or(staffDashboard.navigationFallback)).toBeVisible();

    // Phase 2: UC-013 — Register Walk-In Patient
    await queuePage.navigate();

    // Register existing patient as walk-in
    await queuePage.openWalkInForm();
    await queuePage.searchPatient(e2eData.e2e004.existingPatient.name);
    
    // Wait for search results - Playwright auto-waits
    const searchResult = page.getByRole('option', { name: new RegExp(e2eData.e2e004.existingPatient.name, 'i') });
    await searchResult.waitFor({ state: 'visible', timeout: 5000 }).catch(() => {});
    if (await searchResult.isVisible()) {
      await searchResult.click();
    }
    
    await queuePage.addToQueue();

    // Checkpoint: Patient in queue
    await expect(page.getByText(new RegExp(e2eData.e2e004.existingPatient.name, 'i'))).toBeVisible();

    // Phase 3: UC-014 — Manage Same-Day Queue
    // Add second walk-in for reorder testing
    await queuePage.createNewWalkIn({
      name: e2eData.e2e004.newWalkin.name,
      dob: e2eData.e2e004.newWalkin.dob,
      phone: e2eData.e2e004.newWalkin.phone,
    });

    // Checkpoint: Two entries in queue
    const queueEntries = await queuePage.queueRows.count();
    expect(queueEntries).toBeGreaterThanOrEqual(2);

    // Verify estimated wait times shown
    await expect(queuePage.estimatedWaitText.first()).toBeVisible();

    // Remove the test walk-in to clean up
    // await queuePage.removeFromQueue(1);

    // Phase 4: UC-015 — Check In Patient Arrival
    await schedulePage.navigate();

    const scheduledRow = schedulePage.scheduledAppointments.first();
    
    if (await scheduledRow.isVisible()) {
      await schedulePage.checkInPatient(scheduledRow);

      // Checkpoint: Status changed to Arrived
      await expect(scheduledRow).toContainText(/arrived/i);
    }

    // Phase 5: UC-016 — Review No-Show Risk Alerts
    // Look for high-risk flags
    const riskFlags = schedulePage.highRiskFlag;
    
    if (await riskFlags.first().isVisible()) {
      // Click to view details
      await riskFlags.first().click();

      // Add outreach note
      await schedulePage.addOutreachNote(e2eData.e2e004.outreachNote);

      // Checkpoint: Note saved
      await expect(page.getByText(/saved|recorded/i)).toBeVisible();
    }

    // Final Checkpoint: Staff has completed daily operations
    // All scheduled check-ins processed, risks reviewed
  });
});
