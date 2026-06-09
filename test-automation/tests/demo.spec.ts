import { test, expect } from '@playwright/test';

/**
 * DEMO-001: Standalone Playwright Demo
 * This test demonstrates Playwright working without needing the local app
 */
test.describe('Demo: Playwright Test Execution', () => {
  
  test('DEMO-001: Search on Playwright website @demo', async ({ page }) => {
    // Step 1: Navigate to Playwright docs
    await page.goto('https://playwright.dev/');
    
    // Step 2: Verify page loaded
    await expect(page).toHaveTitle(/Playwright/);
    
    // Step 3: Click search button
    const searchButton = page.getByRole('button', { name: /search/i });
    await searchButton.click();
    
    // Step 4: Type search query
    const searchInput = page.getByPlaceholder(/search/i);
    await searchInput.fill('locator');
    
    // Step 5: Verify search results appear
    await expect(page.getByText(/Locators/i).first()).toBeVisible();
    
    console.log('✅ DEMO-001 completed successfully!');
  });

  test('DEMO-002: Form interaction example @demo', async ({ page }) => {
    // Navigate to example form page
    await page.goto('https://www.w3schools.com/html/html_forms.asp');
    
    // Find and fill first name input
    const firstNameInput = page.locator('input[name="fname"]').first();
    await firstNameInput.fill('Test');
    
    // Find and fill last name input  
    const lastNameInput = page.locator('input[name="lname"]').first();
    await lastNameInput.fill('User');
    
    // Verify values were entered
    await expect(firstNameInput).toHaveValue('Test');
    await expect(lastNameInput).toHaveValue('User');
    
    console.log('✅ DEMO-002 completed successfully!');
  });

  test('DEMO-003: API request example @demo', async ({ request }) => {
    // Make API call to public endpoint
    const response = await request.get('https://jsonplaceholder.typicode.com/users/1');
    
    // Verify response
    expect(response.ok()).toBeTruthy();
    expect(response.status()).toBe(200);
    
    const user = await response.json();
    expect(user.name).toBe('Leanne Graham');
    expect(user.email).toBe('Sincere@april.biz');
    
    console.log('✅ DEMO-003 completed successfully!');
    console.log(`   User: ${user.name} (${user.email})`);
  });
});
