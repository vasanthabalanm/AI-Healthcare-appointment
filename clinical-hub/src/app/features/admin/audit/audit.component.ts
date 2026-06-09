import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuditService, AuditLogEntry, AuditFilters } from '../../../core/services/audit.service';

const ACTION_BADGE_MAP: Record<string, string> = {
  CHECK_IN: 'check-in',
  INTAKE_SUBMIT: 'intake',
  CODE_ACCEPT: 'code',
  CODE_MODIFY: 'code',
  USER_DEACTIVATE: 'user',
  USER_CREATE: 'user',
  DOCUMENT_UPLOAD: 'upload',
  APPOINTMENT_CANCEL: 'cancel',
};

@Component({
  selector: 'app-admin-audit',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
<div class="page">
  <div class="page-header">
    <h1>Audit log</h1>
    <div style="display:flex;align-items:center;gap:12px;">
      <span class="export-loading" *ngIf="exporting" aria-live="polite">
        <span class="spinner"></span> Exporting CSV…
      </span>
      <button class="btn btn--secondary" (click)="exportCsv()" [disabled]="exporting"
              aria-label="Export audit log as CSV">
        ↑ Export CSV
      </button>
    </div>
  </div>

  <!-- Filter bar -->
  <form class="filter-bar" role="search" aria-label="Audit log filters" (ngSubmit)="applyFilters()">
    <div class="filter-field">
      <label for="filter-from">Date from</label>
      <input type="date" id="filter-from" [(ngModel)]="filters.from" name="from">
    </div>
    <div class="filter-field">
      <label for="filter-to">Date to</label>
      <input type="date" id="filter-to" [(ngModel)]="filters.to" name="to">
    </div>
    <div class="filter-field">
      <label for="filter-actor">Actor</label>
      <input type="text" id="filter-actor" [(ngModel)]="filters.actor" name="actor"
             placeholder="All actors" style="width:160px;">
    </div>
    <div class="filter-field">
      <label for="filter-action">Action type</label>
      <select id="filter-action" [(ngModel)]="filters.action" name="action">
        <option value="">All actions</option>
        <option value="CHECK_IN">CHECK_IN</option>
        <option value="INTAKE_SUBMIT">INTAKE_SUBMIT</option>
        <option value="CODE_ACCEPT">CODE_ACCEPT</option>
        <option value="USER_DEACTIVATE">USER_DEACTIVATE</option>
        <option value="DOCUMENT_UPLOAD">DOCUMENT_UPLOAD</option>
        <option value="APPOINTMENT_CANCEL">APPOINTMENT_CANCEL</option>
      </select>
    </div>
    <div class="filter-actions">
      <button class="btn btn--primary" type="submit">Apply filters</button>
      <button class="btn btn--secondary" type="button" (click)="clearFilters()">Clear</button>
    </div>
  </form>

  <!-- Table -->
  <div class="log-table" role="table" aria-label="Audit log entries" *ngIf="!loading && entries.length">
    <div class="log-table-header" role="row">
      <span role="columnheader">Timestamp</span>
      <span role="columnheader">Actor</span>
      <span role="columnheader">Action</span>
      <span role="columnheader">Details</span>
      <span role="columnheader">Entity ID</span>
    </div>
    <div *ngFor="let e of entries" class="log-row" role="row">
      <span class="log-ts" role="cell">{{ formatDate(e.timestamp) }}<br>{{ formatTime(e.timestamp) }} UTC</span>
      <div role="cell">
        <p class="log-actor">{{ e.actorName }} <small>({{ e.actorId }})</small></p>
        <p class="log-role">{{ e.actorRole }}</p>
      </div>
      <span role="cell">
        <span class="action-badge" [class]="'action-badge--' + badgeClass(e.action)">{{ e.action }}</span>
      </span>
      <span class="log-detail" role="cell" [innerHTML]="e.detail"></span>
      <span class="log-entity" role="cell">{{ e.entityId }}</span>
    </div>

    <!-- Pagination -->
    <div class="pagination">
      <span class="pagination-info">
        Showing {{ (currentPage - 1) * pageSize + 1 }}–{{ Math.min(currentPage * pageSize, total) }} of {{ total }}
      </span>
      <div class="pagination-btns">
        <button class="page-btn" (click)="goToPage(currentPage - 1)" [disabled]="currentPage === 1"
                aria-label="Previous page">←</button>
        <button *ngFor="let p of pages" class="page-btn" [class.active]="p === currentPage"
                (click)="goToPage(p)" [attr.aria-current]="p === currentPage ? 'page' : null">{{ p }}</button>
        <button class="page-btn" (click)="goToPage(currentPage + 1)" [disabled]="currentPage === totalPages"
                aria-label="Next page">→</button>
      </div>
    </div>
  </div>

  <!-- Empty state -->
  <div class="empty" *ngIf="!loading && !entries.length">
    <h3>No audit entries found</h3>
    <p>Try adjusting your filters or date range.</p>
  </div>

  <p *ngIf="loading" style="text-align:center;padding:48px;color:var(--ct2);" aria-live="polite">Loading…</p>

  <p class="immutable-notice" *ngIf="!loading && entries.length">
    🔒 Audit records are immutable and cannot be modified or deleted.
  </p>
</div>
  `,
  styles: [`
    :host{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--cw:#D4820A;--cwb:#FEF5E7;--ci:#1C6EA4;--cib:#E8F0F8;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .page{padding:32px;font-family:var(--ff);font-size:14px;color:var(--ct1);}
    .page-header{display:flex;align-items:center;justify-content:space-between;margin-bottom:24px;}
    h1{font-size:24px;font-weight:600;}
    .btn{display:inline-flex;align-items:center;gap:8px;padding:8px 16px;border-radius:var(--r2);font-size:13px;font-weight:500;cursor:pointer;border:1px solid transparent;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover{background:var(--cph);}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover{background:var(--cs1);}
    .filter-bar{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:16px 20px;margin-bottom:16px;display:flex;align-items:flex-end;gap:16px;flex-wrap:wrap;}
    .filter-field{display:flex;flex-direction:column;gap:6px;}
    .filter-field label{font-size:12px;font-weight:500;color:var(--ct2);}
    .filter-field input,.filter-field select{padding:7px 10px;border:1px solid var(--cb);border-radius:var(--r2);font-size:13px;font-family:var(--ff);}
    .filter-field input:focus,.filter-field select:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .filter-actions{display:flex;gap:8px;align-items:flex-end;}
    .log-table{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);overflow:hidden;}
    .log-table-header{display:grid;grid-template-columns:160px 160px 140px 1fr 120px;padding:10px 20px;border-bottom:2px solid var(--cb);font-size:12px;font-weight:600;color:var(--ct2);text-transform:uppercase;letter-spacing:.04em;}
    .log-row{display:grid;grid-template-columns:160px 160px 140px 1fr 120px;padding:12px 20px;border-bottom:1px solid var(--cs2);align-items:start;}
    .log-row:last-child{border-bottom:none;}
    .log-row:hover{background:var(--cs1);}
    .log-ts{font-size:12px;font-family:monospace;color:var(--ct2);}
    .log-actor{font-size:13px;font-weight:500;margin:0;}
    .log-actor small{font-size:11px;font-weight:400;color:var(--ct2);}
    .log-role{font-size:11px;color:var(--ctd);margin:0;}
    .action-badge{display:inline-flex;align-items:center;padding:2px 8px;border-radius:var(--r2);font-size:11px;font-weight:600;font-family:monospace;}
    .action-badge--check-in{background:var(--cokb);color:var(--cok);}
    .action-badge--intake{background:var(--cib);color:var(--ci);}
    .action-badge--code{background:var(--cps);color:var(--cp);}
    .action-badge--user{background:var(--cwb);color:var(--cw);}
    .action-badge--upload{background:var(--cs2);color:var(--ct2);}
    .action-badge--cancel{background:var(--ceb);color:var(--ce);}
    .log-detail{font-size:13px;color:var(--ct2);}
    .log-entity{font-size:11px;font-family:monospace;color:var(--ctd);}
    .pagination{display:flex;align-items:center;justify-content:space-between;padding:14px 20px;border-top:1px solid var(--cs2);}
    .pagination-info{font-size:13px;color:var(--ct2);}
    .pagination-btns{display:flex;gap:8px;}
    .page-btn{padding:5px 12px;border:1px solid var(--cb);border-radius:var(--r2);background:var(--cs0);cursor:pointer;font-size:13px;}
    .page-btn.active{background:var(--cp);color:#fff;border-color:var(--cp);}
    .page-btn:hover:not(.active){background:var(--cs1);}
    .immutable-notice{font-size:12px;color:var(--ctd);margin-top:12px;}
    .empty{padding:64px 32px;text-align:center;color:var(--ct2);}
    .empty h3{font-size:16px;font-weight:600;margin-bottom:8px;}
    .export-loading{font-size:13px;color:var(--ct2);display:flex;align-items:center;gap:8px;}
    .spinner{width:14px;height:14px;border:2px solid var(--cs2);border-top-color:var(--cp);border-radius:50%;animation:spin .8s linear infinite;display:inline-block;}
    @keyframes spin{to{transform:rotate(360deg);}}
  `]
})
export class AuditComponent implements OnInit {
  entries: AuditLogEntry[] = [];
  loading = true;
  exporting = false;
  total = 0;
  currentPage = 1;
  pageSize = 20;
  filters: AuditFilters = {};
  Math = Math;

  constructor(private auditSvc: AuditService) {}

  ngOnInit(): void { this.loadPage(); }

  get totalPages() { return Math.ceil(this.total / this.pageSize); }
  get pages() {
    const start = Math.max(1, this.currentPage - 2);
    const end = Math.min(this.totalPages, this.currentPage + 2);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  }

  private loadPage(): void {
    this.loading = true;
    this.auditSvc.getLog({ ...this.filters, page: this.currentPage, pageSize: this.pageSize }).subscribe({
      next: result => {
        this.entries = result.items;
        this.total = result.total;
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  applyFilters(): void {
    this.currentPage = 1;
    this.loadPage();
  }

  clearFilters(): void {
    this.filters = {};
    this.currentPage = 1;
    this.loadPage();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.currentPage = page;
    this.loadPage();
  }

  exportCsv(): void {
    this.exporting = true;
    this.auditSvc.exportCsv(this.filters).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        this.exporting = false;
      },
      error: () => { this.exporting = false; }
    });
  }

  badgeClass(action: string): string {
    return ACTION_BADGE_MAP[action] ?? 'upload';
  }

  formatDate(iso: string): string {
    return iso.slice(0, 10);
  }

  formatTime(iso: string): string {
    return iso.slice(11, 19);
  }
}

