import { test, expect } from '@playwright/test';
import { LoginPage, RegisterPage, PatientDashboardPage, StaffDashboardPage } from '../../pages';
import authData from '../../data/auth.json';

test.describe('Authentication Feature Tests', () => {
  let loginPage: LoginPage;
  let registerPage: RegisterPage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    registerPage = new RegisterPage(page);
  });

  // FT-001: Patient registers with valid data
  test('FT-001: Patient registers with valid data @P0', async ({ page }) => {
    await registerPage.navigate();
    
    await registerPage.register({
      firstName: authData.newPatient.firstName,
      lastName: authData.newPatient.lastName,
      dob: authData.newPatient.dob,
      phone: authData.newPatient.phone,
      email: `test-${Date.now()}@test.dev`, // Unique email
      password: authData.newPatient.password,
    });

    // Success: redirects to "Check your inbox" page
    await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible();
  });

  // FT-002: Registration with duplicate email → 409
  test('FT-002: Duplicate email registration returns error @P0', async ({ page }) => {
    await registerPage.navigate();
    
    await registerPage.register({
      firstName: 'Another',
      lastName: 'Person',
      dob: '1985-03-10',
      phone: '555-0102',
      email: authData.duplicateEmail,
      password: authData.newPatient.password,
    });

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByRole('alert')).toContainText(/already|registered|exists/i);
  });

  // FT-003: Email verification link activates account
  test.skip('FT-003: Email verification activates account @P0', async ({ page }) => {
    // Requires SMTP stub integration - skipped until stub is configured
    // Navigate to verification URL with valid token
    // Assert email verified message
  });

  // FT-004: Expired verification token rejected
  test.skip('FT-004: Expired token rejected @P1', async ({ page }) => {
    // Requires seeding expired token
    // Assert error message indicates expiration
  });

  // FT-005: Staff login — happy path
  test('FT-005: Staff login succeeds @P0', async ({ page }) => {
    await loginPage.navigate();
    await loginPage.login(
      authData.credentials.staff.email,
      authData.credentials.staff.password
    );

    await expect(page).toHaveURL(/\/staff\//);
    
    const staffDashboard = new StaffDashboardPage(page);
    await expect(staffDashboard.navigation).toBeVisible();
  });

  // FT-006: Patient login — happy path
  test('FT-006: Patient login succeeds @P0', async ({ page }) => {
    await loginPage.navigate();
    await loginPage.login(
      authData.credentials.patient.email,
      authData.credentials.patient.password
    );

    await expect(page).toHaveURL(/\/patient\//);
    
    const patientDashboard = new PatientDashboardPage(page);
    await expect(patientDashboard.heading).toBeVisible();
  });

  // FT-007: Invalid credentials → generic error
  test('FT-007: Invalid credentials show generic error @P0', async ({ page }) => {
    await loginPage.navigate();
    await loginPage.login(
      authData.invalidCredentials.email,
      authData.invalidCredentials.password
    );

    await expect(page.getByRole('alert')).toBeVisible();
    
    // Verify no role/account disclosure (OWASP A07)
    const alertText = await page.getByRole('alert').textContent();
    for (const forbidden of authData.forbiddenErrorSubstrings) {
      expect(alertText?.toLowerCase()).not.toContain(forbidden);
    }
    
    await expect(page).toHaveURL(/\/login/);
  });

  // FT-008: Patient JWT accessing Staff route → 403
  test('FT-008: Patient cannot access staff routes @P0 @security', async ({ page, request }) => {
    // Login as patient
    await loginPage.navigate();
    await loginPage.login(
      authData.credentials.patient.email,
      authData.credentials.patient.password
    );
    await expect(page).toHaveURL(/\/patient\//);

    // Try to access staff queue via API
    const token = await page.evaluate(() => localStorage.getItem('access_token'));
    const response = await request.get('http://localhost:5153/api/staff/queue', {
      headers: { Authorization: `Bearer ${token}` },
    });
    
    expect(response.status()).toBe(403);
  });

  // FT-009: Password reset via email token
  test.skip('FT-009: Password reset flow @P1', async ({ page }) => {
    // Requires SMTP stub integration
    // Navigate to forgot password
    // Submit email, retrieve token, reset password
    // Login with new password
  });
});
