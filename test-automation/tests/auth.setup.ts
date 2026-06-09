import { test as setup, expect } from '@playwright/test';
import { LoginPage } from '../pages';
import authData from '../data/auth.json';

const PATIENT_AUTH_FILE = 'playwright/.auth/patient.json';
const STAFF_AUTH_FILE = 'playwright/.auth/staff.json';
const ADMIN_AUTH_FILE = 'playwright/.auth/admin.json';

setup('authenticate as patient', async ({ page }) => {
  const loginPage = new LoginPage(page);
  await loginPage.navigate();
  await loginPage.login(authData.credentials.patient.email, authData.credentials.patient.password);
  
  await expect(page).toHaveURL(/\/patient\//);
  await page.context().storageState({ path: PATIENT_AUTH_FILE });
});

setup('authenticate as staff', async ({ page }) => {
  const loginPage = new LoginPage(page);
  await loginPage.navigate();
  await loginPage.login(authData.credentials.staff.email, authData.credentials.staff.password);
  
  await expect(page).toHaveURL(/\/staff\//);
  await page.context().storageState({ path: STAFF_AUTH_FILE });
});

setup('authenticate as admin', async ({ page }) => {
  const loginPage = new LoginPage(page);
  await loginPage.navigate();
  await loginPage.login(authData.credentials.admin.email, authData.credentials.admin.password);
  
  await expect(page).toHaveURL(/\/admin\//);
  await page.context().storageState({ path: ADMIN_AUTH_FILE });
});
