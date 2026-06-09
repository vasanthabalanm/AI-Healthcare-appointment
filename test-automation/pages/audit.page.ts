import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Admin Audit Log page object
 */
export class AuditPage extends BasePage {
  readonly path = '/admin/audit';

  constructor(page: Page) {
    super(page);
  }

  get auditTable(): Locator {
    return this.page.getByRole('table', { name: /audit/i });
  }

  get auditTableFallback(): Locator {
    return this.page.getByTestId('audit-table');
  }

  get auditEntries(): Locator {
    return this.page.getByRole('row');
  }

  get dateFromFilter(): Locator {
    return this.page.getByLabel('Date From');
  }

  get dateToFilter(): Locator {
    return this.page.getByLabel('Date To');
  }

  get actorFilter(): Locator {
    return this.page.getByLabel(/actor/i);
  }

  get actionTypeFilter(): Locator {
    return this.page.getByLabel('Action Type');
  }

  get entityTypeFilter(): Locator {
    return this.page.getByLabel(/entity/i);
  }

  get filterButton(): Locator {
    return this.page.getByRole('button', { name: /filter|apply/i });
  }

  get editButton(): Locator {
    return this.page.getByRole('button', { name: /edit/i });
  }

  get deleteButton(): Locator {
    return this.page.getByRole('button', { name: /delete/i });
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async filterByDate(from: string, to?: string): Promise<void> {
    await this.dateFromFilter.fill(from);
    if (to) {
      await this.dateToFilter.fill(to);
    }
    await this.filterButton.click();
  }

  async filterByActionType(action: string): Promise<void> {
    await this.actionTypeFilter.selectOption(action);
    await this.filterButton.click();
  }

  async filterByActor(actorId: string): Promise<void> {
    await this.actorFilter.fill(actorId);
    await this.filterButton.click();
  }

  getEntryById(entryId: string): Locator {
    return this.page.getByRole('row', { name: new RegExp(entryId) });
  }
}
