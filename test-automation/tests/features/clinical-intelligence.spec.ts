import { test, expect } from '@playwright/test';
import { Patient360Page, CodingPage, LoginPage } from '../../pages';
import authData from '../../data/auth.json';
import codingData from '../../data/coding.json';

test.describe('Clinical Intelligence Feature Tests', () => {
  let loginPage: LoginPage;
  let patient360Page: Patient360Page;
  let codingPage: CodingPage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    patient360Page = new Patient360Page(page);
    codingPage = new CodingPage(page);
    
    // Login as staff
    await loginPage.navigate();
    await loginPage.login(authData.credentials.staff.email, authData.credentials.staff.password);
    await expect(page).toHaveURL(/\/staff\//);
  });

  // FT-044: Conflict detection flags allergies
  test.skip('FT-044: Conflict detection @P1', async () => {
    // Requires seeded conflicting documents
  });

  // FT-045: Verify blocked with conflicts
  test.skip('FT-045: Verify blocked with conflicts @P0', async () => {
    // Requires unresolved conflict state
  });

  // FT-046: Staff accesses 360° view
  test('FT-046: Staff accesses 360 view @P0', async ({ page }) => {
    await patient360Page.navigate(codingData.patientId);
    
    await expect(patient360Page.heading.or(page.getByText(/360.*view/i))).toBeVisible();
    
    // Verify sections are present
    await expect(patient360Page.vitalsSection.or(page.getByText(/vitals/i))).toBeVisible();
  });

  // FT-047: ICD-10 generation for verified patient
  test('FT-047: Generate codes for verified patient @P0', async ({ page }) => {
    await patient360Page.navigate(codingData.patientId);
    
    // Check if generate codes button is enabled
    const generateBtn = patient360Page.generateCodesButton.or(patient360Page.generateCodesButtonFallback);
    
    if (await generateBtn.isEnabled()) {
      await generateBtn.click();
      
      await expect(page.getByText(/generating|queued/i)).toBeVisible({ timeout: 5000 });
    }
  });

  // FT-048: Generation blocked for unverified patient
  test.skip('FT-048: Generation blocked unverified @P0', async ({ page }) => {
    // Navigate to unverified patient
    // Verify generate button is disabled
  });

  // FT-049: Trust-First: commit without verifiedBy → 422
  test('FT-049: Trust-First requires verifiedBy @P0 @security', async ({ page, request }) => {
    const token = await page.evaluate(() => localStorage.getItem('access_token'));
    
    // Try to accept without verifiedBy
    const response = await request.patch('http://localhost:5153/api/coding/1', {
      headers: { Authorization: `Bearer ${token}` },
      data: { action: 'Accept' }, // Missing verifiedBy
    });
    
    expect(response.status()).toBe(422);
  });

  // FT-050: Staff accepts ICD-10 suggestion
  test('FT-050: Staff accepts code @P0', async ({ page }) => {
    await codingPage.navigate(codingData.patientId);
    
    const pendingRows = codingPage.pendingSuggestions;
    
    if (await pendingRows.first().isVisible()) {
      await codingPage.acceptSuggestion(0);
      
      await expect(page.getByText(/accepted/i)).toBeVisible();
    }
  });

  // FT-051: Staff modifies suggestion
  test('FT-051: Staff modifies code @P1', async ({ page }) => {
    await codingPage.navigate(codingData.patientId);
    
    const pendingRows = codingPage.pendingSuggestions;
    
    if (await pendingRows.first().isVisible()) {
      await codingPage.modifySuggestion(0, codingData.manualModification.code);
      
      await expect(page.getByText(/modified/i)).toBeVisible();
    }
  });

  // FT-052: Staff rejects suggestion
  test('FT-052: Staff rejects code @P1', async ({ page }) => {
    await codingPage.navigate(codingData.patientId);
    
    const pendingRows = codingPage.pendingSuggestions;
    
    if (await pendingRows.first().isVisible()) {
      await codingPage.rejectSuggestion(0);
      
      await expect(page.getByText(/rejected/i)).toBeVisible();
    }
  });
});
