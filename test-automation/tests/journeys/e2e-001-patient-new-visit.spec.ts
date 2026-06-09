import { test, expect } from '@playwright/test';
import { LoginPage, RegisterPage, AppointmentsPage, IntakePage, PatientDashboardPage } from '../../pages';
import e2eData from '../../data/e2e-journeys.json';

/**
 * E2E-001: Patient New Visit Journey
 * UC Chain: UC-001 → UC-002 → UC-004 → UC-009
 * Business Value: Complete patient onboarding from registration through intake
 */
test.describe('E2E-001: Patient New Visit Journey', () => {
  // Shared state across journey phases
  let patientEmail: string;
  let patientPassword: string;

  const API_BASE = 'http://localhost:5153';

  test('Patient completes new visit journey @P0 @e2e', async ({ page, request }) => {
    const registerPage = new RegisterPage(page);
    const loginPage = new LoginPage(page);
    const appointmentsPage = new AppointmentsPage(page);
    const intakePage = new IntakePage(page);

    // Generate unique email for this test run
    patientEmail = `e2e-${Date.now()}@test.dev`;
    patientPassword = e2eData.e2e001.patient.password;

    // Phase 1: UC-001 — Patient Self-Registration
    // Entry Point: /register
    await registerPage.navigate();

    await registerPage.register({
      firstName: e2eData.e2e001.patient.firstName,
      lastName: e2eData.e2e001.patient.lastName,
      dob: e2eData.e2e001.patient.dob,
      phone: e2eData.e2e001.patient.phone,
      email: patientEmail,
      password: patientPassword,
    });

    // Checkpoint: Registration success - "Check your inbox" page
    await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible();

    // Phase 1b: Email verification via dev endpoint
    // DevEmailService captures the verification URL; retrieve it via /dev/email/{email}
    // Retry a few times in case the email hasn't been stored yet
    let emailData: { actionUrl?: string } = {};
    for (let attempt = 0; attempt < 5; attempt++) {
      const emailResponse = await request.get(`${API_BASE}/dev/email/${encodeURIComponent(patientEmail)}`);
      if (emailResponse.ok()) {
        emailData = await emailResponse.json();
        break;
      }
      // Wait 500ms before retry
      await page.waitForTimeout(500);
    }
    
    if (!emailData.actionUrl) {
      throw new Error(
        `Could not retrieve verification email for ${patientEmail}. ` +
        `Ensure the backend is running with DevEmailService (no SMTP_HOST env var) ` +
        `and the /dev/email endpoint is available.`
      );
    }
    
    // Navigate to verification URL to activate account
    await page.goto(emailData.actionUrl);
    
    // Checkpoint: Account verified - should redirect to login or show success
    await expect(page.getByText(/verified|activated|success/i).or(page.getByRole('link', { name: /login|sign in/i }))).toBeVisible();

    // Phase 2: UC-002 — Login & Session Establishment
    // Entry Point: /login
    await loginPage.navigate();
    await loginPage.login(patientEmail, patientPassword);

    // Checkpoint: Patient dashboard loaded
    await expect(page).toHaveURL(/\/patient\//);
    
    const patientDashboard = new PatientDashboardPage(page);
    await expect(patientDashboard.heading).toBeVisible();

    // Shared Data: JWT captured
    const jwt = await page.evaluate(() => localStorage.getItem('access_token'));
    expect(jwt).toBeTruthy();

    // Phase 3: UC-004 — Book Available Appointment
    // Entry Point: /patient/appointments/book
    await appointmentsPage.navigateToBook();

    await appointmentsPage.selectFirstAvailableDate();
    await appointmentsPage.selectFirstAvailableSlot();

    // Checkpoint: Summary panel visible - use unique heading
    await expect(page.getByRole('heading', { name: 'Booking summary' })).toBeVisible();

    await appointmentsPage.confirmBooking();

    // Checkpoint: Booking confirmed
    await expect(page.getByText(/confirmed|booked|scheduled/i)).toBeVisible();

    // Phase 4: UC-009 — Submit Manual Intake Form
    // Navigate directly to manual intake (no appointment ID required)
    await page.goto('/patient/intake/manual');

    await intakePage.fillManualIntake({
      chiefComplaint: e2eData.e2e001.intake.chiefComplaint,
      medicalHistory: e2eData.e2e001.intake.medicalHistory,
      currentMedications: e2eData.e2e001.intake.currentMeds,
      allergies: e2eData.e2e001.intake.allergies,
    });

    // Note: Insurance fields are not present on the manual intake form

    await intakePage.submitIntake();

    // Final Checkpoint: Intake submitted - wait for navigation away from form or success toast
    // The form should redirect or show a success message after submission
    await expect(
      page.getByText(/intake submitted|successfully submitted|submission successful/i)
        .or(page.getByRole('heading', { name: /thank you|submission received/i }))
        .or(page.getByRole('alert').filter({ hasText: /success/i }))
    ).toBeVisible({ timeout: 15000 });
  });
});
