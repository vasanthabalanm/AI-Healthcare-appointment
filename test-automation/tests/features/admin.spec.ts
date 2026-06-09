import { test, expect } from '@playwright/test';
import { AdminUsersPage, AuditPage, LoginPage } from '../../pages';
import adminData from '../../data/admin.json';
import authData from '../../data/auth.json';

test.describe('Admin Feature Tests', () => {
  let loginPage: LoginPage;
  let adminUsersPage: AdminUsersPage;
  let auditPage: AuditPage;

  test.beforeEach(async ({ page }) => {
    loginPage = new LoginPage(page);
    adminUsersPage = new AdminUsersPage(page);
    auditPage = new AuditPage(page);
    
    // Login as admin
    await loginPage.navigate();
    await loginPage.login(authData.credentials.admin.email, authData.credentials.admin.password);
    await expect(page).toHaveURL(/\/admin\//);
  });

  // FT-010: Admin creates Staff account
  test('FT-010: Admin creates Staff account @P1', async ({ page }) => {
    await adminUsersPage.navigate();
    
    const uniqueEmail = `newstaff-${Date.now()}@clinicalhub.dev`;
    await adminUsersPage.createUser({
      email: uniqueEmail,
      firstName: adminData.newStaff.firstName,
      lastName: adminData.newStaff.lastName,
      role: adminData.newStaff.role,
    });

    // Verify user appears in list (not a table row, just text)
    await expect(page.getByText(uniqueEmail)).toBeVisible();
  });

  // FT-011: Admin deactivates Staff account
  test('FT-011: Admin deactivates Staff account @P1', async ({ page }) => {
    await adminUsersPage.navigate();
    
    // First create a user to deactivate
    const uniqueEmail = `deactivate-${Date.now()}@clinicalhub.dev`;
    await adminUsersPage.createUser({
      email: uniqueEmail,
      firstName: 'Temp',
      lastName: 'Staff',
      role: 'staff',
    });
    
    await expect(page.getByText(uniqueEmail)).toBeVisible();
    
    // Deactivate the user
    await adminUsersPage.deactivateUser(uniqueEmail);
    
    // Wait for status to update - look for Inactive status near the email
    await expect(page.getByText(uniqueEmail).locator('..').getByText(/inactive/i)).toBeVisible();
  });

  // FT-012: Cannot deactivate last Admin
  test('FT-012: Cannot deactivate last admin @P0', async ({ page, request }) => {
    await adminUsersPage.navigate();
    
    // Try to deactivate via API (assuming single admin)
    const token = await page.evaluate(() => localStorage.getItem('access_token'));
    const response = await request.patch('http://localhost:5153/api/admin/users/1/deactivate', {
      headers: { Authorization: `Bearer ${token}` },
    });
    
    // Should return 409 conflict
    expect(response.status()).toBe(409);
  });

  // FT-053: Admin reviews audit log
  test('FT-053: Admin reviews audit log with filters @P1', async ({ page }) => {
    await auditPage.navigate();
    
    await expect(auditPage.auditTable.or(auditPage.auditTableFallback)).toBeVisible();
    
    // Verify at least one entry exists
    const entries = await auditPage.auditEntries.count();
    expect(entries).toBeGreaterThan(0);
    
    // Apply filter by action type
    await auditPage.filterByActionType('UPDATE');
    
    // Verify filtered entries contain UPDATE action
    const filteredEntries = auditPage.auditEntries.filter({ hasText: /update/i });
    await expect(filteredEntries.first()).toBeVisible();
  });

  // FT-054: Modify audit entry → 405
  test('FT-054: Audit log modification blocked @P0 @security', async ({ page, request }) => {
    await auditPage.navigate();
    
    const token = await page.evaluate(() => localStorage.getItem('access_token'));
    
    // Attempt DELETE on audit entry
    const deleteResponse = await request.delete('http://localhost:5153/api/admin/audit/1', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(deleteResponse.status()).toBe(405);
    
    // Attempt PATCH on audit entry
    const patchResponse = await request.patch('http://localhost:5153/api/admin/audit/1', {
      headers: { Authorization: `Bearer ${token}` },
      data: { note: 'modified' },
    });
    expect(patchResponse.status()).toBe(405);
  });
});
