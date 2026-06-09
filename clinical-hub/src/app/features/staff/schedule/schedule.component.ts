import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AppointmentService, ScheduleEntry } from '../../../core/services/appointment.service';

@Component({
  selector: 'app-staff-schedule',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
<!-- Page header -->
<div class="page-header">
  <div>
    <h1>Daily schedule</h1>
    <p class="page-date">{{ dateLabel() }}</p>
  </div>
  <div style="display:flex;gap:12px;align-items:center;">
    <input type="date" class="date-input" [formControl]="dateCtrl" (change)="load()" aria-label="Select date" />
    <a class="btn btn--secondary" routerLink="/staff/walkin">Register walk-in</a>
    <a class="btn btn--primary" routerLink="/staff/queue">View queue</a>
  </div>
</div>

<!-- Filter bar -->
<div class="filter-bar">
  <label class="filter-toggle">
    <span
      class="toggle-switch"
      [class.on]="highRiskOnly()"
      role="switch"
      [attr.aria-checked]="highRiskOnly()"
      (click)="toggleHighRisk()"
      tabindex="0"
      (keydown.enter)="toggleHighRisk()"
      (keydown.space)="toggleHighRisk()"
    ></span>
    <span>High-risk only ({{ highRiskCount() }})</span>
  </label>
</div>

@if (loading()) {
  <p class="page-sub">Loading schedule…</p>
}

@if (!loading() && error()) {
  <p class="text-danger" role="alert">{{ error() }}</p>
}

@if (!loading() && !error() && displayed().length === 0) {
  <div class="empty" role="status">
    <svg width="36" height="36" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z" clip-rule="evenodd"/></svg>
    <p>No appointments for this date{{ highRiskOnly() ? ' (high-risk filter active)' : '' }}.</p>
  </div>
}

@if (!loading() && displayed().length > 0) {
  <div class="schedule-table" role="table" aria-label="Daily appointment schedule">
    <div class="schedule-table-header" role="row">
      <span role="columnheader">Time</span>
      <span role="columnheader">Patient</span>
      <span role="columnheader">Status</span>
      <span role="columnheader">Intake</span>
      <span role="columnheader">Risk</span>
      <span role="columnheader">Actions</span>
    </div>
    @for (item of displayed(); track item.appointmentId) {
      <div class="schedule-row" [class.high-risk-row]="item.riskFlag" role="row">
        <span class="time-cell" role="cell">{{ item.slotTime | date:'h:mm a' }}</span>
        <div role="cell">
          <a class="patient-link" [routerLink]="['/staff/patients', item.patientId, 'view360']"
             [attr.aria-label]="'View 360 profile for ' + item.patientName">
            {{ item.patientName }}
          </a>
        </div>
        <span role="cell">
          <span class="badge" [ngClass]="statusBadge(item.status)">{{ item.status }}</span>
        </span>
        <span role="cell">
          <span class="badge" [ngClass]="intakeBadge(item.intakeStatus)">{{ item.intakeStatus }}</span>
        </span>
        <span role="cell">
          @if (item.riskFlag) {
            <span class="risk-badge" aria-label="High no-show risk">⚠ High</span>
          } @else {
            <span class="risk-badge risk-badge--low" aria-label="Low no-show risk">Low</span>
          }
        </span>
        <div class="row-actions" role="cell">
          @if (item.status === 'Scheduled') {
            <button class="btn btn--primary btn--sm"
                    [disabled]="checkInBusy() === item.appointmentId"
                    (click)="checkIn(item)"
                    [attr.aria-label]="'Check in ' + item.patientName">
              {{ checkInBusy() === item.appointmentId ? '…' : 'Check In' }}
            </button>
          }
          @if (item.status === 'Arrived') {
            <span class="badge badge--arrived">Arrived ✓</span>
          }
        </div>
      </div>
    }
  </div>
  <p class="table-footer">Showing {{ displayed().length }} of {{ totalCount() }} appointments</p>
}

<!-- Toast -->
@if (toast()) {
  <div class="toast show" role="status" aria-live="polite">{{ toast() }}</div>
}
  `,
  styles: [`
    :root{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cw:#D4820A;--cwb:#FEF5E7;--cok:#1A7A4A;--cokb:#E8F5EE;--ci:#1C6EA4;--cib:#E8F0F8;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .page-header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:16px;flex-wrap:wrap;gap:12px;}
    h1{font-size:24px;font-weight:600;color:var(--ct1);}
    .page-date{font-size:14px;color:var(--ct2);margin-top:2px;}
    .btn{display:inline-flex;align-items:center;gap:6px;padding:8px 16px;border-radius:var(--r2);font-size:13px;font-weight:500;cursor:pointer;border:1px solid transparent;text-decoration:none;}
    .btn:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .btn:disabled{opacity:.5;cursor:not-allowed;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not(:disabled){background:var(--cph);}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover:not(:disabled){background:var(--cs1);}
    .btn--sm{padding:4px 12px;font-size:12px;}
    .date-input{padding:7px 10px;border:1px solid var(--cb);border-radius:var(--r2);font-size:13px;font-family:var(--ff);}
    .date-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .filter-bar{display:flex;align-items:center;gap:16px;margin-bottom:16px;}
    .filter-toggle{display:flex;align-items:center;gap:8px;font-size:13px;color:var(--ct2);cursor:pointer;user-select:none;}
    .toggle-switch{position:relative;width:36px;height:20px;background:var(--cs2);border-radius:10px;cursor:pointer;transition:background .2s;flex-shrink:0;}
    .toggle-switch.on{background:var(--cp);}
    .toggle-switch::after{content:'';position:absolute;top:2px;left:2px;width:16px;height:16px;border-radius:50%;background:#fff;transition:left .2s;}
    .toggle-switch.on::after{left:18px;}
    .schedule-table{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);overflow:hidden;margin-bottom:8px;}
    .schedule-table-header{display:grid;grid-template-columns:80px 200px 120px 100px 80px 1fr;padding:10px 20px;border-bottom:2px solid var(--cb);font-size:12px;font-weight:600;color:var(--ct2);text-transform:uppercase;letter-spacing:.04em;}
    .schedule-row{display:grid;grid-template-columns:80px 200px 120px 100px 80px 1fr;padding:14px 20px;border-bottom:1px solid var(--cs2);align-items:center;}
    .schedule-row:last-child{border-bottom:none;}
    .schedule-row:hover{background:var(--cs1);}
    .high-risk-row{background:var(--cwb)!important;}
    .time-cell{font-size:14px;font-weight:600;color:var(--ct1);}
    .patient-link{color:var(--cp);text-decoration:none;font-weight:500;font-size:13px;}
    .patient-link:hover{text-decoration:underline;}
    .badge{display:inline-flex;align-items:center;padding:2px 8px;border-radius:var(--r2);font-size:12px;font-weight:500;}
    .badge--scheduled,.badge-scheduled{background:var(--cib);color:var(--ci);}
    .badge--arrived,.badge-arrived{background:var(--cokb);color:var(--cok);}
    .badge--completed,.badge-completed{background:var(--cs2);color:var(--ct2);}
    .badge--cancelled,.badge-cancelled{background:var(--cs2);color:var(--ct2);}
    .badge--submitted{background:var(--cokb);color:var(--cok);}
    .badge--pending{background:var(--cwb);color:var(--cw);}
    .badge--na{background:var(--cs2);color:var(--ctd);}
    .risk-badge{display:inline-flex;align-items:center;gap:4px;padding:2px 8px;border-radius:var(--r2);font-size:12px;font-weight:600;background:var(--cwb);color:var(--cw);}
    .risk-badge--low{background:var(--cokb);color:var(--cok);}
    .row-actions{display:flex;gap:6px;}
    .empty{padding:64px 32px;text-align:center;color:var(--ct2);}
    .empty svg{margin:0 auto 12px;display:block;color:var(--ctd);}
    .empty p{font-size:14px;}
    .page-sub{font-size:14px;color:var(--ct2);padding:16px 0;}
    .text-danger{color:var(--ce);font-size:14px;}
    .table-footer{font-size:12px;color:var(--ctd);}
    .toast{position:fixed;bottom:80px;left:50%;transform:translateX(-50%);background:var(--ct1);color:#fff;padding:12px 20px;border-radius:var(--r2);font-size:13px;z-index:600;display:none;}
    .toast.show{display:block;}
  `]
})
export class ScheduleComponent implements OnInit {
  dateCtrl      = new FormControl(this.todayIso());
  items         = signal<ScheduleEntry[]>([]);
  loading       = signal(false);
  error         = signal<string | null>(null);
  highRiskOnly  = signal(false);
  checkInBusy   = signal<number | null>(null);
  toast         = signal('');
  totalCount    = signal(0);

  readonly highRiskCount = computed(() => this.items().filter(i => i.riskFlag).length);
  readonly displayed     = computed(() =>
    this.highRiskOnly() ? this.items().filter(i => i.riskFlag) : this.items()
  );
  readonly dateLabel     = computed(() => {
    const d = this.dateCtrl.value ? new Date(this.dateCtrl.value + 'T00:00:00') : new Date();
    return d.toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  });

  constructor(private svc: AppointmentService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    const date = this.dateCtrl.value ?? undefined;
    this.loading.set(true);
    this.error.set(null);
    this.svc.getStaffSchedule(date ?? undefined).subscribe({
      next: res => {
        this.items.set(res.data);
        this.totalCount.set(res.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load schedule. Please try again.');
        this.loading.set(false);
      }
    });
  }

  checkIn(item: ScheduleEntry): void {
    this.checkInBusy.set(item.appointmentId);
    this.svc.checkIn(item.appointmentId).subscribe({
      next: () => {
        this.checkInBusy.set(null);
        item.status = 'Arrived';
        this.showToast(`${item.patientName} checked in.`);
        this.items.update(list => [...list]);
      },
      error: err => {
        this.checkInBusy.set(null);
        this.showToast(err?.error?.error ?? 'Check-in failed.');
      }
    });
  }

  statusBadge(status: string): Record<string, boolean> {
    return {
      'badge--scheduled': status === 'Scheduled',
      'badge--arrived':   status === 'Arrived',
      'badge--completed': status === 'Completed',
      'badge--cancelled': status === 'Cancelled',
    };
  }

  intakeBadge(s: string): Record<string, boolean> {
    return { 'badge--submitted': s === 'Submitted', 'badge--pending': s === 'Pending', 'badge--na': s === 'NA' };
  }

  toggleHighRisk(): void { this.highRiskOnly.update(v => !v); }

  private showToast(msg: string): void {
    this.toast.set(msg);
    setTimeout(() => this.toast.set(''), 3500);
  }

  private todayIso(): string {
    return new Date().toISOString().split('T')[0];
  }
}

