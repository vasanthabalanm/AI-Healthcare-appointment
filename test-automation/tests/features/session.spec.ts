import { test, expect } from '@playwright/test';
import { LoginPage } from '../../pages';
import authData from '../../data/auth.json';

test.describe('Session Security Feature Tests', () => {
  // FT-055: Session timeout → 401
  test('FT-055: Session timeout returns 401 @P0 @security', async ({ page, request }) => {
    const loginPage = new LoginPage(page);
    
    // Login first
    await loginPage.navigate();
    await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
    await expect(page).toHaveURL(/\/patient\//);
    
    // Simulate expired token by clearing localStorage
    await page.evaluate(() => localStorage.removeItem('access_token'));
    
    // Try to access protected route
    const response = await request.get('http://localhost:5153/api/patient/appointments', {
      headers: { Authorization: 'Bearer invalid-token' },
    });
    
    expect(response.status()).toBe(401);
  });

  test('FT-055b: UI redirects to login on session timeout @P0 @security', async ({ page }) => {
    const loginPage = new LoginPage(page);
    
    // Login
    await loginPage.navigate();
    await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
    await expect(page).toHaveURL(/\/patient\//);
    
    // Clear token to simulate timeout
    await page.evaluate(() => localStorage.removeItem('access_token'));
    
    // Navigate to protected page
    await page.goto('/patient/appointments');
    
    // Should redirect to login
    await expect(page).toHaveURL(/\/login/);
  });
});
