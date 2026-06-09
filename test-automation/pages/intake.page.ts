import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Intake form page object
 */
export class IntakePage extends BasePage {
  constructor(page: Page) {
    super(page);
  }

  getPath(appointmentId: string): string {
    return `/patient/intake/${appointmentId}`;
  }

  get manualIntakeButton(): Locator {
    return this.page.getByRole('button', { name: /manual/i });
  }

  get aiIntakeButton(): Locator {
    return this.page.getByRole('button', { name: /ai-assisted/i });
  }

  get chiefComplaintInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Reason for visit' });
  }

  get chiefComplaintInputFallback(): Locator {
    return this.page.getByTestId('input-chief-complaint');
  }

  get medicalHistoryInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Relevant history' });
  }

  get currentMedicationsInput(): Locator {
    // First medication input in the dynamic list
    return this.page.getByRole('textbox', { name: 'Medication 1' });
  }

  get allergiesInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Known allergies' });
  }

  get insuranceProviderInput(): Locator {
    return this.page.getByLabel('Insurance Provider');
  }

  get insuranceIdInput(): Locator {
    return this.page.getByLabel('Insurance ID');
  }

  get submitButton(): Locator {
    return this.page.getByRole('button', { name: /submit/i });
  }

  get saveButton(): Locator {
    return this.page.getByRole('button', { name: /save/i });
  }

  get confirmButton(): Locator {
    return this.page.getByRole('button', { name: /confirm|submit/i });
  }

  get chatInput(): Locator {
    return this.page.getByRole('textbox');
  }

  get summaryReviewPanel(): Locator {
    return this.page.getByTestId('intake-summary');
  }

  get validationError(): Locator {
    return this.page.locator('.validation-error, [class*="error"]');
  }

  async navigate(appointmentId: string): Promise<void> {
    await this.goto(this.getPath(appointmentId));
  }

  async selectManualIntake(): Promise<void> {
    await this.manualIntakeButton.click();
  }

  async selectAiIntake(): Promise<void> {
    await this.aiIntakeButton.click();
  }

  async fillChiefComplaint(complaint: string): Promise<void> {
    await this.chiefComplaintInput.fill(complaint);
  }

  async fillMedicalHistory(history: string): Promise<void> {
    await this.medicalHistoryInput.fill(history);
  }

  async fillCurrentMedications(meds: string): Promise<void> {
    await this.currentMedicationsInput.fill(meds);
  }

  async fillAllergies(allergies: string): Promise<void> {
    await this.allergiesInput.fill(allergies);
  }

  async fillInsurance(provider: string, id: string): Promise<void> {
    await this.insuranceProviderInput.fill(provider);
    await this.insuranceIdInput.fill(id);
  }

  async submitIntake(): Promise<void> {
    await this.submitButton.click();
  }

  async fillManualIntake(data: {
    chiefComplaint: string;
    medicalHistory: string;
    currentMedications: string;
    allergies: string;
  }): Promise<void> {
    await this.fillChiefComplaint(data.chiefComplaint);
    await this.fillMedicalHistory(data.medicalHistory);
    await this.fillCurrentMedications(data.currentMedications);
    await this.fillAllergies(data.allergies);
  }

  async sendChatMessage(message: string): Promise<void> {
    await this.chatInput.fill(message);
    await this.chatInput.press('Enter');
  }
}
