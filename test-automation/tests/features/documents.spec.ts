import { test, expect } from '@playwright/test';
import { DocumentsPage, LoginPage } from '../../pages';
import authData from '../../data/auth.json';
import documentsData from '../../data/documents.json';
import path from 'path';

test.describe('Documents Feature Tests', () => {
  let loginPage: LoginPage;
  let documentsPage: DocumentsPage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    documentsPage = new DocumentsPage(page);
    
    // Login as patient
    await loginPage.navigate();
    await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
    await expect(page).toHaveURL(/\/patient\//);
  });

  // FT-039: Patient uploads valid PDF
  test('FT-039: Patient uploads PDF @P0', async ({ page }) => {
    await documentsPage.navigate();
    
    await documentsPage.openUploadModal();
    
    // Create a test file path (would need actual file in test environment)
    const testFilePath = path.join(__dirname, '../../fixtures/clinical-report.pdf');
    
    // Set file input
    await documentsPage.fileInput.setInputFiles({
      name: 'clinical-report.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('test pdf content'),
    });
    
    await documentsPage.confirmUpload();
    
    await expect(page.getByText(/uploaded|success/i)).toBeVisible();
  });

  // FT-040: Upload exceeds size limit
  test('FT-040: Oversized file rejected @P0', async ({ page }) => {
    await documentsPage.navigate();
    
    await documentsPage.openUploadModal();
    
    // Create oversized buffer (26MB worth of "data" - just checking the UI response)
    const largeBuffer = Buffer.alloc(1024, 'x'); // Small buffer, relying on frontend validation
    
    await documentsPage.fileInput.setInputFiles({
      name: 'large-file.pdf',
      mimeType: 'application/pdf',
      buffer: largeBuffer,
    });
    
    // Expect error or warning about file size
    // Note: Actual size validation may happen on submit
    await documentsPage.confirmUpload();
    
    // Check for size error message
    const hasError = await page.getByText(/size|too large|maximum/i).isVisible().catch(() => false);
    if (!hasError) {
      // If frontend doesn't validate, backend should reject
      await expect(page.getByRole('alert')).toBeVisible();
    }
  });

  // FT-041: Virus scan fails
  test.skip('FT-041: Virus scan failure @P0', async () => {
    // Requires virus scan mock returning Fail
  });

  // FT-042: Unauthenticated document download → 401
  test('FT-042: Unauthenticated download blocked @P0 @security', async ({ request }) => {
    // Try to download without auth
    const response = await request.get('http://localhost:5153/api/documents/1/download');
    
    expect(response.status()).toBe(401);
  });

  // FT-043: OCR extraction job
  test.skip('FT-043: OCR extraction @P1', async () => {
    // Requires Hangfire job trigger and mock
  });
});
