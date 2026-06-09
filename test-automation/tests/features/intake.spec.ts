import { test, expect } from '@playwright/test';
import { IntakePage, LoginPage } from '../../pages';
import authData from '../../data/auth.json';
import appointmentsData from '../../data/appointments.json';

test.describe('Intake Feature Tests', () => {
  let loginPage: LoginPage;
  let intakePage: IntakePage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    intakePage = new IntakePage(page);
    
    // Login as patient
    await loginPage.navigate();
    await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
    await expect(page).toHaveURL(/\/patient\//);
  });

  // FT-024: AI intake multi-turn session
  test('FT-024: AI intake conversation @P1', async ({ page }) => {
    // Mock the AI intake API endpoints
    let messageCount = 0;
    
    // Mock session start
    await page.route('**/intake/ai/start', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: 'test-session-123',
          message: "Hello! I'm your intake assistant. What's the main reason for your visit today?"
        })
      });
    });
    
    // Mock message responses - simulates multi-turn conversation
    await page.route('**/intake/ai/message', async route => {
      messageCount++;
      
      const responses: Record<number, object> = {
        1: {
          text: "I understand you're experiencing headaches. How long have you had them?",
          confidence: 0.25,
          fieldCommitted: true,
          confirmedFields: { chiefComplaint: 'Persistent headaches for 2 weeks' },
          requiresClarification: false
        },
        2: {
          text: "Thank you. Are you currently taking any medications?",
          confidence: 0.50,
          fieldCommitted: true,
          confirmedFields: { 
            chiefComplaint: 'Persistent headaches for 2 weeks',
            medicalHistory: 'Previous migraines, no surgeries'
          },
          requiresClarification: false
        },
        3: {
          text: "Got it. Do you have any allergies to medications?",
          confidence: 0.75,
          fieldCommitted: true,
          confirmedFields: {
            chiefComplaint: 'Persistent headaches for 2 weeks',
            medicalHistory: 'Previous migraines, no surgeries',
            currentMeds: 'Ibuprofen 400mg as needed'
          },
          requiresClarification: false
        },
        4: {
          text: "Perfect! I have all the information I need.",
          confidence: 0.95,
          fieldCommitted: true,
          confirmedFields: {
            chiefComplaint: 'Persistent headaches for 2 weeks',
            medicalHistory: 'Previous migraines, no surgeries',
            currentMeds: 'Ibuprofen 400mg as needed',
            allergies: 'No known allergies'
          },
          requiresClarification: false
        }
      };
      
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(responses[messageCount] || responses[4])
      });
    });
    
    // Mock submit
    await page.route('**/intake/ai/complete', async route => {
      await route.fulfill({ status: 200, body: '{}' });
    });
    
    // Navigate to AI intake page
    await page.goto('/patient/intake');
    
    // Verify AI greeting appears - use heading role to avoid matching message bubbles
    await expect(page.getByRole('heading', { name: 'Intake assistant' })).toBeVisible();
    await expect(page.getByText(/main reason for your visit/i)).toBeVisible();
    
    // Turn 1: Describe symptoms
    const chatInput = page.getByRole('textbox', { name: /your message/i });
    await chatInput.fill('I have been having persistent headaches for 2 weeks');
    await page.getByRole('button', { name: 'Send' }).click();
    
    // Verify AI response and summary update - target summary card, not chat message
    await expect(page.getByText(/experiencing headaches/i)).toBeVisible();
    await expect(page.locator('.section-card__value').filter({ hasText: /Persistent headaches/i })).toBeVisible();
    
    // Turn 2: Medical history
    await chatInput.fill('I have had migraines before but no surgeries');
    await page.getByRole('button', { name: 'Send' }).click();
    await expect(page.getByText(/taking any medications/i)).toBeVisible();
    
    // Turn 3: Medications
    await chatInput.fill('Just ibuprofen 400mg when needed');
    await page.getByRole('button', { name: 'Send' }).click();
    await expect(page.getByText(/any allergies/i)).toBeVisible();
    
    // Turn 4: Allergies - completes intake
    await chatInput.fill('No known allergies');
    await page.getByRole('button', { name: 'Send' }).click();
    
    // Verify all sections filled (100% progress)
    await expect(page.getByText('100%')).toBeVisible();
    
    // Submit button should be enabled
    const submitButton = page.getByRole('button', { name: /submit intake/i }).first();
    await expect(submitButton).toBeEnabled();
    
    // Submit the intake
    await submitButton.click();
    
    // Verify success message
    await expect(page.getByText(/intake submitted/i)).toBeVisible();
  });

  // FT-025: AI clarification when confidence < 0.70
  test('FT-025: AI clarification prompt @P1', async ({ page }) => {
    // Mock session start
    await page.route('**/intake/ai/start', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: 'test-session-456',
          message: "Hello! I'm your intake assistant. What's the main reason for your visit today?"
        })
      });
    });
    
    let clarificationGiven = false;
    
    // Mock message with low confidence requiring clarification
    await page.route('**/intake/ai/message', async route => {
      if (!clarificationGiven) {
        // First response - low confidence, needs clarification
        clarificationGiven = true;
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            text: "I'm not quite sure I understood. Could you tell me more specifically about your symptoms?",
            confidence: 0.45,
            fieldCommitted: false,
            confirmedFields: {},
            requiresClarification: true
          })
        });
      } else {
        // After clarification - high confidence
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            text: "Thank you for clarifying! I understand now.",
            confidence: 0.85,
            fieldCommitted: true,
            confirmedFields: { chiefComplaint: 'Sharp chest pain when breathing deeply' },
            requiresClarification: false
          })
        });
      }
    });
    
    // Navigate to AI intake
    await page.goto('/patient/intake');
    
    // Send vague message
    const chatInput = page.getByRole('textbox', { name: /your message/i });
    await chatInput.fill('I feel bad');
    await page.getByRole('button', { name: 'Send' }).click();
    
    // Verify clarification request
    await expect(page.getByText(/not quite sure|could you tell me more/i)).toBeVisible();
    
    // Progress should still be 0% since field wasn't committed
    await expect(page.getByText('0%')).toBeVisible();
    
    // Provide clearer response
    await chatInput.fill('I have sharp chest pain when I breathe deeply');
    await page.getByRole('button', { name: 'Send' }).click();
    
    // Now field should be filled - target summary section, not chat message
    await expect(page.locator('.section-card__value').filter({ hasText: /Sharp chest pain/i })).toBeVisible();
    await expect(page.getByText('25%')).toBeVisible();
  });

  // FT-026: Manual intake form submission
  test('FT-026: Manual intake submission @P0', async ({ page }) => {
    // This test requires a valid appointmentId - using placeholder
    const testAppointmentId = 'test-appointment-id';
    
    await intakePage.navigate(testAppointmentId);
    
    // Check if manual button exists and click it
    if (await intakePage.manualIntakeButton.isVisible()) {
      await intakePage.selectManualIntake();
    }
    
    await intakePage.fillManualIntake({
      chiefComplaint: appointmentsData.intake.chiefComplaint,
      medicalHistory: appointmentsData.intake.medicalHistory,
      currentMedications: appointmentsData.intake.currentMeds,
      allergies: appointmentsData.intake.allergies,
    });
    
    await intakePage.submitIntake();
    
    await expect(page.getByText(/submitted|saved/i)).toBeVisible();
  });

  // FT-027: Missing required field → 422
  test('FT-027: Missing required field blocked @P0', async ({ page }) => {
    const testAppointmentId = 'test-appointment-id';
    
    await intakePage.navigate(testAppointmentId);
    
    if (await intakePage.manualIntakeButton.isVisible()) {
      await intakePage.selectManualIntake();
    }
    
    // Fill everything except chief complaint
    await intakePage.fillMedicalHistory(appointmentsData.intake.medicalHistory);
    await intakePage.fillCurrentMedications(appointmentsData.intake.currentMeds);
    await intakePage.fillAllergies(appointmentsData.intake.allergies);
    
    await intakePage.submitIntake();
    
    // Should show validation error
    await expect(intakePage.validationError.or(page.getByText(/required/i))).toBeVisible();
  });

  // FT-028: Intake edit increments version
  test.skip('FT-028: Intake version increment @P1', async () => {
    // Requires existing intake to edit
  });

  // FT-029: Insurance pre-check match
  test('FT-029: Insurance validation passes @P2', async ({ page }) => {
    const testAppointmentId = 'test-appointment-id';
    
    await intakePage.navigate(testAppointmentId);
    
    if (await intakePage.manualIntakeButton.isVisible()) {
      await intakePage.selectManualIntake();
    }
    
    await intakePage.fillInsurance(
      appointmentsData.insurance.valid.provider,
      appointmentsData.insurance.valid.id
    );
    
    // Trigger validation by tabbing
    await intakePage.insuranceIdInput.press('Tab');
    
    // Look for validated badge
    await expect(page.getByText(/validated/i).or(page.locator('[class*="success"]'))).toBeVisible({ timeout: 5000 });
  });

  // FT-030: Insurance pre-check no match
  test('FT-030: Insurance not verified warning @P1', async ({ page }) => {
    const testAppointmentId = 'test-appointment-id';
    
    await intakePage.navigate(testAppointmentId);
    
    if (await intakePage.manualIntakeButton.isVisible()) {
      await intakePage.selectManualIntake();
    }
    
    await intakePage.fillInsurance(
      appointmentsData.insurance.invalid.provider,
      appointmentsData.insurance.invalid.id
    );
    
    await intakePage.insuranceIdInput.press('Tab');
    
    // Warning should be visible but form still submittable
    await expect(page.getByText(/not verified|warning/i).or(page.locator('[class*="warning"]'))).toBeVisible({ timeout: 5000 });
    
    // Submit button should still be enabled
    await expect(intakePage.submitButton).toBeEnabled();
  });
});
