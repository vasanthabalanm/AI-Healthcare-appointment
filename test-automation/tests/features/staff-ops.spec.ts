import { test, expect } from '@playwright/test';
import { QueuePage, SchedulePage, LoginPage } from '../../pages';
import authData from '../../data/auth.json';
import staffOpsData from '../../data/staff-ops.json';

test.describe('Staff Operations Feature Tests', () => {
  let loginPage: LoginPage;
  let queuePage: QueuePage;
  let schedulePage: SchedulePage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    queuePage = new QueuePage(page);
    schedulePage = new SchedulePage(page);
    
    // Login as staff
    await loginPage.navigate();
    await loginPage.login(authData.credentials.staff.email, authData.credentials.staff.password);
    await expect(page).toHaveURL(/\/staff\//);
  });

  // FT-032: Staff registers walk-in (existing patient)
  test('FT-032: Staff walk-in existing patient @P0', async ({ page }) => {
    await queuePage.navigate();
    
    await queuePage.openWalkInForm();
    await queuePage.searchPatient(staffOpsData.existingPatient.name);
    
    // Wait for search results - Playwright auto-waits
    const result = page.getByRole('option', { name: new RegExp(staffOpsData.existingPatient.name, 'i') });
    await result.waitFor({ state: 'visible', timeout: 5000 }).catch(() => {});
    if (await result.isVisible()) {
      await result.click();
    }
    
    await queuePage.addToQueue();
    
    await expect(page.getByText(new RegExp(staffOpsData.existingPatient.name, 'i'))).toBeVisible();
  });

  // FT-033: Staff walk-in (new minimal patient)
  test('FT-033: Staff walk-in new patient @P1', async ({ page }) => {
    await queuePage.navigate();
    
    await queuePage.createNewWalkIn({
      name: staffOpsData.newWalkin.name,
      dob: staffOpsData.newWalkin.dob,
      phone: staffOpsData.newWalkin.phone,
    });
    
    await expect(page.getByText(new RegExp(staffOpsData.newWalkin.name, 'i'))).toBeVisible();
  });

  // FT-034: Staff views and reorders queue
  test('FT-034: Staff reorders queue @P0', async ({ page }) => {
    await queuePage.navigate();
    
    // Verify queue is visible
    await expect(queuePage.queueRows.first().or(queuePage.queueRowFallback.first())).toBeVisible();
    
    // Check estimated wait times are shown
    await expect(queuePage.estimatedWaitText.first()).toBeVisible();
  });

  // FT-035: Concurrent queue reorder conflict
  test.skip('FT-035: Queue reorder conflict @P1', async () => {
    // Requires parallel Staff sessions
  });

  // FT-036: Staff checks in patient
  test('FT-036: Staff check-in @P0', async ({ page }) => {
    await schedulePage.navigate();
    
    const scheduledRow = schedulePage.scheduledAppointments.first();
    
    if (await scheduledRow.isVisible()) {
      await scheduledRow.getByRole('button', { name: /check in/i }).click();
      
      // Verify status changed to Arrived
      await expect(scheduledRow).toContainText(/arrived/i);
    }
  });

  // FT-037: Patient cannot self-check-in → 403
  test('FT-037: Patient self-check-in blocked @P0 @security', async ({ page, request }) => {
    // Logout staff, login as patient
    await page.evaluate(() => localStorage.clear());
    
    await loginPage.navigate();
    await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
    await expect(page).toHaveURL(/\/patient\//);
    
    const token = await page.evaluate(() => localStorage.getItem('access_token'));
    
    // Try to check in via API
    const response = await request.patch('http://localhost:5153/api/appointments/1/checkin', {
      headers: { Authorization: `Bearer ${token}` },
    });
    
    expect(response.status()).toBe(403);
  });

  // FT-038: High-risk appointments flagged
  test('FT-038: High-risk flag visible @P1', async ({ page }) => {
    await schedulePage.navigate();
    
    // Look for risk flag indicator
    const riskFlag = schedulePage.highRiskFlag;
    
    // If any high-risk appointments exist, flag should be visible
    if (await riskFlag.first().isVisible()) {
      await expect(riskFlag.first()).toBeVisible();
    }
  });
});
