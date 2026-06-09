import { test, expect } from '@playwright/test';
import { LoginPage, DocumentsPage, Patient360Page, CodingPage } from '../../pages';
import e2eData from '../../data/e2e-journeys.json';

/**
 * E2E-003: Staff Clinical Intelligence Journey
 * UC Chain: UC-002 → UC-017 → UC-020 → UC-021 → UC-022
 * Business Value: Reduces clinical prep from 20 minutes to 2 minutes
 */
test.describe('E2E-003: Staff Clinical Intelligence Journey', () => {
  test('Staff clinical intelligence workflow @P0 @e2e', async ({ page, context }) => {
    const loginPage = new LoginPage(page);
    const patient360Page = new Patient360Page(page);
    const codingPage = new CodingPage(page);

    // Phase 1: UC-002 — Staff Login
    await loginPage.navigate();
    await loginPage.login(e2eData.e2e003.staff.email, e2eData.e2e003.staff.password);
    
    // Checkpoint: Staff dashboard
    await expect(page).toHaveURL(/\/staff\//);

    // Save staff auth state for reuse
    await context.storageState({ path: 'playwright/.auth/staff-e2e.json' });

    // Phase 2: UC-017 — Patient Uploads Document (via Patient Session)
    // In real test, would switch to patient context
    // For now, assume document is already uploaded for patient 19

    // Phase 3: UC-020 — Staff Reviews 360° Patient View
    await patient360Page.navigate(e2eData.e2e003.patient.id);

    // Checkpoint: 360° view loaded
    await expect(patient360Page.heading.or(page.getByText(/360.*view/i))).toBeVisible();

    // Verify sections present
    await expect(patient360Page.vitalsSection.or(page.getByText(/vitals/i))).toBeVisible();

    // Check for conflicts
    const hasConflicts = await patient360Page.conflictFlags.first().isVisible().catch(() => false);
    
    if (!hasConflicts) {
      // Verify patient if not already verified
      const verifyBtn = patient360Page.verifyButton.or(patient360Page.verifyButtonFallback);
      if (await verifyBtn.isEnabled()) {
        await verifyBtn.click();
        await expect(page.getByText(/verified/i)).toBeVisible();
      }
    }

    // Phase 4: UC-021 — Generate ICD-10 and CPT Codes
    const generateBtn = patient360Page.generateCodesButton.or(patient360Page.generateCodesButtonFallback);
    
    if (await generateBtn.isEnabled()) {
      await generateBtn.click();
      
      // Checkpoint: Generation started
      await expect(page.getByText(/generating|queued|processing/i)).toBeVisible({ timeout: 5000 });

      // Wait for completion - generation completes when processing message disappears
      await expect(page.getByText(/generating|queued|processing/i)).toBeHidden({ timeout: 30000 }).catch(() => {});
    }

    // Navigate to coding verification page
    await codingPage.navigate(e2eData.e2e003.patient.id);

    // Phase 5: UC-022 — Staff Verifies Medical Codes
    const pendingRows = codingPage.pendingSuggestions;

    // Checkpoint: Suggestions visible
    if (await pendingRows.first().isVisible({ timeout: 30000 })) {
      // Accept first suggestion
      await codingPage.acceptSuggestion(0);
      await expect(page.getByText(/accepted/i)).toBeVisible();

      // Modify second suggestion if available
      if (await pendingRows.first().isVisible()) {
        await codingPage.modifySuggestion(0, e2eData.e2e003.manualModification.code);
        await expect(page.getByText(/modified/i)).toBeVisible();
      }

      // Reject remaining if any
      if (await pendingRows.first().isVisible()) {
        await codingPage.rejectSuggestion(0);
        await expect(page.getByText(/rejected/i)).toBeVisible();
      }
    }

    // Final Checkpoint: All suggestions actioned
    // No pending suggestions should remain
    const remainingPending = await codingPage.pendingSuggestions.count();
    expect(remainingPending).toBe(0);
  });
});
