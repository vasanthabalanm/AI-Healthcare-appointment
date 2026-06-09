import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AppointmentService, Appointment } from '../../../core/services/appointment.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-patient-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, DatePipe],
  template: `
<!-- Welcome header -->
<div class="welcome-header">
  <p class="welcome-date">{{ today | date:'EEEE, MMMM d, y' }}</p>
  <h1 class="welcome-title">Welcome back, {{ firstName() }}</h1>
</div>

<!-- Summary cards row (SCR-005) -->
<div class="summary-row">
  <!-- Next appointment -->
  <div class="summary-card">
    <div class="summary-card__label">Next appointment</div>
    <div class="summary-card__value">
      <ng-container *ngIf="!loading() && active(); else noAppt">
        {{ active()!.slotTime | date:'d MMM y' }}
      </ng-container>
      <ng-template #noAppt>
        <span style="color:var(--ctd)">{{ loading() ? '…' : 'None scheduled' }}</span>
      </ng-template>
    </div>
    <div class="summary-card__meta" *ngIf="!loading() && active()">
      {{ active()!.slotTime | date:'h:mm a' }}
    </div>
    <a class="summary-card__link" routerLink="/patient/book">Book new →</a>
  </div>

  <!-- Intake status -->
  <div class="summary-card">
    <div class="summary-card__label">Intake status</div>
    <div class="summary-card__value">
      <span class="status-badge status-badge--pending">Pending</span>
    </div>
    <div class="summary-card__meta">Complete before your appointment</div>
    <a class="summary-card__link" routerLink="/patient/intake">Start now →</a>
  </div>

  <!-- Documents -->
  <div class="summary-card">
    <div class="summary-card__label">Documents uploaded</div>
    <div class="summary-card__value">0</div>
    <div class="summary-card__meta">No documents on file yet</div>
    <a class="summary-card__link" routerLink="/patient/documents">Upload →</a>
  </div>
</div>

<!-- grid-2: left detail, right quick actions -->
<div class="grid-2">
  <!-- Left column -->
  <div class="left-col">
    <!-- Upcoming appointment card -->
    <div class="detail-card" *ngIf="!loading() && active()">
      <div class="detail-card__header">
        <span class="detail-card__title">Upcoming appointment</span>
        <span class="status-badge status-badge--{{ active()!.status.toLowerCase() }}">{{ active()!.status }}</span>
      </div>
      <div class="detail-card__row">
        <svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true" style="color:var(--cp);flex-shrink:0"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z" clip-rule="evenodd"/></svg>
        {{ active()!.slotTime | date:'EEEE, d MMMM y' }}
      </div>
      <div class="detail-card__row">
        <svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true" style="color:var(--cp);flex-shrink:0"><path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm1-12a1 1 0 10-2 0v4a1 1 0 00.293.707l2.828 2.829a1 1 0 101.415-1.415L11 9.586V6z" clip-rule="evenodd"/></svg>
        {{ active()!.slotTime | date:'h:mm a' }}
      </div>
      <div class="detail-card__actions">
        <a class="btn btn--primary btn--sm" routerLink="/patient/appointments">Manage</a>
        <a class="btn btn--secondary btn--sm" routerLink="/patient/waitlist">Join waitlist</a>
      </div>
    </div>

    <div class="detail-card detail-card--empty" *ngIf="!loading() && !active()">
      <svg width="36" height="36" viewBox="0 0 20 20" fill="var(--cb)" aria-hidden="true"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z" clip-rule="evenodd"/></svg>
      <p>No upcoming appointments.</p>
      <a class="btn btn--primary btn--sm" routerLink="/patient/book">Book now</a>
    </div>

    <!-- Intake status card -->
    <div class="detail-card" style="margin-top:16px">
      <div class="detail-card__header">
        <span class="detail-card__title">Intake form</span>
        <span class="status-badge status-badge--pending">Pending</span>
      </div>
      <p style="font-size:13px;color:var(--ct2);margin:0 0 14px">
        Complete your health intake before your next appointment so your care team can prepare.
      </p>
      <a class="btn btn--primary btn--sm" routerLink="/patient/intake">Start AI intake</a>
      <a class="btn btn--secondary btn--sm" routerLink="/patient/intake/manual" style="margin-left:8px">Manual form</a>
    </div>
  </div>

  <!-- Right column: Quick actions -->
  <div class="qa-panel">
    <h2 class="qa-title">Quick actions</h2>
    <div class="qa-list">
      <a class="qa-item" routerLink="/patient/book">
        <div class="qa-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z" clip-rule="evenodd"/></svg>
        </div>
        <span>Book new appointment</span>
      </a>
      <a class="qa-item" routerLink="/patient/intake">
        <div class="qa-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M18 10c0 3.866-3.582 7-8 7a8.841 8.841 0 01-4.083-.98L2 17l1.338-3.123C2.493 12.767 2 11.434 2 10c0-3.866 3.582-7 8-7s8 3.134 8 7zM7 9H5v2h2V9zm8 0h-2v2h2V9zM9 9h2v2H9V9z" clip-rule="evenodd"/></svg>
        </div>
        <span>Complete AI intake</span>
      </a>
      <a class="qa-item" routerLink="/patient/documents">
        <div class="qa-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M3 17a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zM6.293 6.707a1 1 0 010-1.414l3-3a1 1 0 011.414 0l3 3a1 1 0 01-1.414 1.414L11 5.414V13a1 1 0 11-2 0V5.414L7.707 6.707a1 1 0 01-1.414 0z" clip-rule="evenodd"/></svg>
        </div>
        <span>Upload a document</span>
      </a>
      <a class="qa-item" routerLink="/patient/appointments">
        <div class="qa-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor"><path d="M9 2a1 1 0 000 2h2a1 1 0 100-2H9z"/><path fill-rule="evenodd" d="M4 5a2 2 0 012-2 3 3 0 006 0 2 2 0 012 2v11a2 2 0 01-2 2H6a2 2 0 01-2-2V5zm3 4a1 1 0 000 2h.01a1 1 0 100-2H7zm3 0a1 1 0 000 2h3a1 1 0 100-2h-3zm-3 4a1 1 0 100 2h.01a1 1 0 100-2H7zm3 0a1 1 0 100 2h3a1 1 0 100-2h-3z" clip-rule="evenodd"/></svg>
        </div>
        <span>View all appointments</span>
      </a>
    </div>
  </div>
</div>
  `,
  styles: [`
    :host{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .welcome-header{margin-bottom:24px;}
    .welcome-date{font-size:13px;color:var(--ct2);margin:0 0 4px;}
    .welcome-title{font-size:24px;font-weight:600;color:var(--ct1);margin:0;}

    /* Summary row */
    .summary-row{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-bottom:24px;}
    .summary-card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);padding:16px 20px;display:flex;flex-direction:column;gap:4px;}
    .summary-card__label{font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;color:var(--ct2);}
    .summary-card__value{font-size:22px;font-weight:700;color:var(--ct1);margin:4px 0;}
    .summary-card__meta{font-size:12px;color:var(--ct2);}
    .summary-card__link{font-size:12px;color:var(--cp);text-decoration:none;margin-top:auto;padding-top:8px;}
    .summary-card__link:hover{text-decoration:underline;}

    /* Grid-2 */
    .grid-2{display:grid;grid-template-columns:1fr 280px;gap:24px;align-items:start;}
    .left-col{display:flex;flex-direction:column;}

    /* Detail cards */
    .detail-card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);padding:20px;}
    .detail-card--empty{display:flex;flex-direction:column;align-items:center;text-align:center;gap:10px;padding:32px 20px;background:var(--cs1);border-style:dashed;}
    .detail-card--empty p{font-size:13px;color:var(--ct2);margin:0;}
    .detail-card__header{display:flex;align-items:center;justify-content:space-between;margin-bottom:14px;}
    .detail-card__title{font-size:14px;font-weight:600;color:var(--ct1);}
    .detail-card__row{display:flex;align-items:center;gap:8px;font-size:14px;color:var(--ct1);margin-bottom:8px;}
    .detail-card__actions{display:flex;gap:8px;margin-top:16px;}

    /* Status badges */
    .status-badge{display:inline-block;padding:2px 10px;border-radius:10px;font-size:11px;font-weight:600;}
    .status-badge--scheduled{background:#DBEAFE;color:#1E40AF;}
    .status-badge--arrived{background:#D1FAE5;color:#065F46;}
    .status-badge--completed{background:var(--cs2);color:var(--ct2);}
    .status-badge--cancelled{background:#FEE2E2;color:#991B1B;}
    .status-badge--noshow{background:#FEF3C7;color:#92400E;}
    .status-badge--pending{background:#FEF3C7;color:#92400E;}

    /* Quick actions */
    .qa-panel{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);padding:20px;}
    .qa-title{font-size:14px;font-weight:600;color:var(--ct1);margin:0 0 16px;}
    .qa-list{display:flex;flex-direction:column;gap:4px;}
    .qa-item{display:flex;align-items:center;gap:12px;padding:10px 12px;border-radius:var(--r2);text-decoration:none;color:var(--ct1);font-size:14px;font-weight:500;}
    .qa-item:hover{background:var(--cs1);}
    .qa-icon{width:32px;height:32px;background:var(--cp);border-radius:var(--r2);display:flex;align-items:center;justify-content:center;color:#fff;flex-shrink:0;}

    /* Buttons */
    .btn{display:inline-flex;align-items:center;justify-content:center;padding:8px 18px;border:none;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;text-decoration:none;font-family:var(--ff);}
    .btn--sm{padding:6px 14px;font-size:13px;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover{background:var(--cph);}
    .btn--secondary{background:var(--cs0);border:1px solid var(--cb);color:var(--ct1);}
    .btn--secondary:hover{background:var(--cs1);}

    @media(max-width:1024px){.summary-row{grid-template-columns:1fr 1fr;}.grid-2{grid-template-columns:1fr;}}
    @media(max-width:640px){.summary-row{grid-template-columns:1fr;}}
  `]
})
export class DashboardComponent implements OnInit {
  private readonly auth = inject(AuthService);

  active  = signal<Appointment | null>(null);
  loading = signal(true);
  today   = new Date();

  readonly firstName = computed(() => {
    const token = this.auth.getToken();
    if (!token) return 'there';
    const payload = this.auth.decodeToken(token);
    return (payload?.['given_name'] as string | undefined) || 'there';
  });

  constructor(private svc: AppointmentService) {}

  ngOnInit(): void {
    this.svc.getMyAppointments().subscribe({
      next: list => {
        const scheduled = list.find(a => a.status === 'Scheduled') ?? null;
        this.active.set(scheduled);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }
}
