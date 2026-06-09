import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AppointmentService, Appointment, Slot } from '../../../core/services/appointment.service';

@Component({
  selector: 'app-my-appointments',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
<!-- Page header -->
<div class="page-header">
  <h1>My appointments</h1>
  <a class="btn btn--primary" routerLink="/patient/book">
    <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z" clip-rule="evenodd"/></svg>
    Book new appointment
  </a>
</div>

<!-- Tabs -->
<div class="tabs" role="tablist" aria-label="Appointment filter">
  <button class="tab" [class.active]="activeTab()==='upcoming'" role="tab"
          [attr.aria-selected]="activeTab()==='upcoming'"
          (click)="activeTab.set('upcoming')">
    Upcoming ({{ upcomingCount() }})
  </button>
  <button class="tab" [class.active]="activeTab()==='past'" role="tab"
          [attr.aria-selected]="activeTab()==='past'"
          (click)="activeTab.set('past')">
    Past ({{ pastCount() }})
  </button>
  <button class="tab" [class.active]="activeTab()==='all'" role="tab"
          [attr.aria-selected]="activeTab()==='all'"
          (click)="activeTab.set('all')">
    All ({{ appointments().length }})
  </button>
</div>

<!-- Loading -->
@if (loading()) {
  <p class="page-sub" style="padding:32px 0;">Loading appointments…</p>
}

<!-- Appointment list -->
@if (!loading()) {
  @if (filtered().length === 0) {
    <div class="empty" role="status">
      <svg width="40" height="40" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z" fill="currentColor" stroke="none"/></svg>
      <h3>No appointments</h3>
      <p>{{ activeTab() === 'upcoming' ? 'You have no upcoming appointments.' : 'No appointments in this category.' }}</p>
      <a class="btn btn--primary" routerLink="/patient/book" style="margin-top:8px;">Book an appointment</a>
    </div>
  }
  @for (appt of filtered(); track appt.id) {
    <article class="appt-card" role="region" [attr.aria-label]="'Appointment: ' + (appt.slotTime | date:'d MMM y')">
      <div class="appt-card__header">
        <div>
          <p class="appt-card__date">{{ appt.slotTime | date:'EEEE, d MMMM y' }}</p>
          <p class="appt-card__time">{{ appt.slotTime | date:'h:mm a' }} – {{ apptEnd(appt) | date:'h:mm a' }}</p>
        </div>
        <span class="badge" [ngClass]="badgeClass(appt.status)" role="status">{{ appt.status }}</span>
      </div>
      <p class="appt-detail"><strong>Type:</strong> General Consultation</p>
      <p class="appt-detail"><strong>Booked:</strong> {{ appt.bookedAt | date:'d MMM y' }}</p>
      @if (appt.isHighRisk) {
        <p class="risk-note" role="note">
          <svg width="13" height="13" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/></svg>
          High no-show risk ({{ appt.noShowRiskScore }}%)
        </p>
      }
      @if (appt.status === 'Scheduled' && isCancellable(appt)) {
        <div class="appt-actions">
          <button class="btn btn--danger" [disabled]="busy() === appt.id"
                  (click)="openCancel(appt)" aria-label="Cancel this appointment">
            Cancel
          </button>
          <button class="btn btn--secondary" [disabled]="busy() === appt.id"
                  (click)="openReschedule(appt)" aria-label="Reschedule this appointment">
            Reschedule
          </button>
          <a class="btn btn--secondary" routerLink="/patient/waitlist"
             aria-label="Join waitlist for a different slot">Join waitlist</a>
        </div>
      }
      @if (appt.status === 'Scheduled' && !isCancellable(appt)) {
        <p class="cutoff-note">
          <svg width="12" height="12" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92z" clip-rule="evenodd"/></svg>
          Cancellation window has closed (less than 2 hours before appointment)
        </p>
      }

      <!-- Reschedule inline panel -->
      @if (rescheduleTarget()?.id === appt.id) {
        <div class="reschedule-panel" role="region" aria-label="Reschedule appointment">
          <p class="reschedule-panel__title">Select a new date and time</p>
          <div style="display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap;">
            <div>
              <label class="field-label" [for]="'rdate-'+appt.id">New date</label>
              <input [id]="'rdate-'+appt.id" type="date" class="field-input"
                     [formControl]="rDateControl" [min]="today" (change)="loadRescheduleSlots()" />
            </div>
          </div>
          @if (rLoading()) {
            <p class="page-sub" style="margin-top:8px;font-size:13px;">Loading slots…</p>
          }
          @if (!rLoading() && rSlots().length > 0) {
            <div class="slot-list" role="listbox" aria-label="Available time slots">
              @for (slot of rSlots(); track slot.id) {
                <button class="slot-btn" type="button"
                        [class.slot-btn--selected]="rSelected()?.id === slot.id"
                        role="option" [attr.aria-selected]="rSelected()?.id === slot.id"
                        (click)="rSelected.set(slot)">
                  {{ slot.slotTime | date:'h:mm a' }}
                </button>
              }
            </div>
          }
          @if (!rLoading() && rDateControl.value && rSlots().length === 0) {
            <p class="page-sub" style="margin-top:8px;font-size:13px;color:var(--ctd);">No slots available for this date.</p>
          }
          <div class="reschedule-actions">
            <button class="btn btn--primary" [disabled]="!rSelected() || busy() === appt.id"
                    (click)="confirmReschedule(appt)">
              {{ busy() === appt.id ? 'Rescheduling…' : 'Confirm reschedule' }}
            </button>
            <button class="btn btn--secondary" (click)="rescheduleTarget.set(null)">Cancel</button>
          </div>
        </div>
      }
    </article>
  }
}

<!-- Cancel confirmation modal -->
@if (cancelTarget()) {
  <div class="modal-backdrop open" role="dialog" aria-modal="true" aria-labelledby="cancel-modal-title">
    <div class="modal">
      <h2 class="modal__title" id="cancel-modal-title">Cancel appointment?</h2>
      <p class="modal__body">
        Cancel your appointment on
        <strong>{{ cancelTarget()!.slotTime | date:'EEEE, d MMMM y' }}</strong>
        at <strong>{{ cancelTarget()!.slotTime | date:'h:mm a' }}</strong>?
        This action cannot be undone.
      </p>
      <div class="modal__actions">
        <button class="btn btn--secondary" (click)="cancelTarget.set(null)">Keep appointment</button>
        <button class="btn btn--danger" [disabled]="busy() !== null"
                (click)="confirmCancel()">
          {{ busy() !== null ? 'Cancelling…' : 'Yes, cancel' }}
        </button>
      </div>
    </div>
  </div>
}

<!-- Toast -->
@if (toast()) {
  <div class="toast show" role="status" aria-live="polite">{{ toast() }}</div>
}
  `,
  styles: [`
    :root{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cw:#D4820A;--cwb:#FEF5E7;--cok:#1A7A4A;--cokb:#E8F5EE;--ci:#1C6EA4;--cib:#E8F0F8;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .page-header{display:flex;align-items:center;justify-content:space-between;margin-bottom:24px;}
    h1{font-size:24px;font-weight:600;color:var(--ct1);}
    .btn{display:inline-flex;align-items:center;gap:6px;padding:8px 16px;border-radius:var(--r2);font-size:13px;font-weight:500;cursor:pointer;border:1px solid transparent;text-decoration:none;}
    .btn:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .btn:disabled{opacity:.5;cursor:not-allowed;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not(:disabled){background:var(--cph);}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover:not(:disabled){background:var(--cs1);}
    .btn--danger{color:var(--ce);border-color:var(--ce);background:var(--cs0);}
    .btn--danger:hover:not(:disabled){background:var(--ceb);}
    .tabs{display:flex;border-bottom:1px solid var(--cb);margin-bottom:24px;}
    .tab{padding:10px 16px;font-size:14px;cursor:pointer;border-bottom:2px solid transparent;color:var(--ct2);background:none;border-top:none;border-left:none;border-right:none;}
    .tab.active{color:var(--cp);border-bottom-color:var(--cp);font-weight:500;}
    .tab:hover:not(.active){color:var(--ct1);}
    .tab:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .appt-card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:20px;margin-bottom:12px;}
    .appt-card__header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:12px;}
    .appt-card__date{font-size:16px;font-weight:600;color:var(--ct1);}
    .appt-card__time{font-size:13px;color:var(--ct2);margin-top:2px;}
    .badge{display:inline-flex;align-items:center;padding:2px 8px;border-radius:var(--r2);font-size:12px;font-weight:500;}
    .badge--confirmed,.badge-confirmed{background:var(--cib);color:var(--ci);}
    .badge--arrived,.badge-arrived{background:var(--cokb);color:var(--cok);}
    .badge--completed,.badge-completed{background:var(--cs2);color:var(--ct2);}
    .badge--cancelled,.badge-cancelled{background:var(--cs2);color:var(--ct2);}
    .badge--noshow,.badge-noshow{background:var(--ceb);color:var(--ce);}
    .appt-detail{font-size:13px;color:var(--ct2);margin-bottom:4px;}
    .appt-detail strong{color:var(--ct1);}
    .appt-actions{display:flex;gap:8px;margin-top:12px;padding-top:12px;border-top:1px solid var(--cs2);flex-wrap:wrap;}
    .cutoff-note{display:flex;align-items:center;gap:4px;font-size:12px;color:var(--cw);margin-top:8px;}
    .risk-note{display:flex;align-items:center;gap:4px;font-size:12px;color:var(--cw);margin-bottom:6px;}
    /* Reschedule panel */
    .reschedule-panel{margin-top:16px;padding:16px;background:var(--cs1);border-radius:var(--r2);border:1px solid var(--cb);}
    .reschedule-panel__title{font-size:14px;font-weight:600;margin-bottom:12px;}
    .field-label{display:block;font-size:12px;font-weight:500;margin-bottom:4px;color:var(--ct2);}
    .field-input{padding:8px 12px;border:1px solid var(--cb);border-radius:var(--r2);font-size:14px;font-family:var(--ff);}
    .field-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .slot-list{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px;}
    .slot-btn{padding:6px 14px;border:1px solid var(--cb);border-radius:var(--r2);font-size:13px;cursor:pointer;background:var(--cs0);}
    .slot-btn:hover{border-color:var(--cp);color:var(--cp);}
    .slot-btn--selected{border-color:var(--cp);background:var(--cps);color:var(--cp);font-weight:500;}
    .reschedule-actions{display:flex;gap:8px;margin-top:12px;}
    /* Empty state */
    .empty{padding:64px 32px;text-align:center;color:var(--ct2);}
    .empty svg{margin:0 auto 16px;display:block;color:var(--ctd);}
    .empty h3{font-size:16px;font-weight:600;color:var(--ct2);margin-bottom:8px;}
    .empty p{font-size:14px;margin-bottom:16px;}
    .page-sub{font-size:14px;color:var(--ct2);}
    /* Modal */
    .modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:500;display:flex;align-items:center;justify-content:center;}
    .modal{background:var(--cs0);border-radius:var(--r3);padding:24px;max-width:440px;width:90%;}
    .modal__title{font-size:18px;font-weight:600;margin-bottom:8px;}
    .modal__body{font-size:14px;color:var(--ct2);margin-bottom:24px;line-height:1.5;}
    .modal__actions{display:flex;gap:12px;justify-content:flex-end;}
    /* Toast */
    .toast{position:fixed;bottom:80px;left:50%;transform:translateX(-50%);background:var(--ct1);color:#fff;padding:12px 20px;border-radius:var(--r2);font-size:13px;z-index:600;display:none;}
    .toast.show{display:block;}
  `]
})
export class AppointmentsComponent implements OnInit {
  readonly today = new Date().toISOString().split('T')[0];

  appointments    = signal<Appointment[]>([]);
  loading         = signal(false);
  busy            = signal<number | null>(null);
  toast           = signal('');

  activeTab       = signal<'upcoming' | 'past' | 'all'>('upcoming');
  cancelTarget    = signal<Appointment | null>(null);
  rescheduleTarget = signal<Appointment | null>(null);
  rDateControl     = new FormControl(this.today, Validators.required);
  rSlots           = signal<Slot[]>([]);
  rSelected        = signal<Slot | null>(null);
  rLoading         = signal(false);

  readonly upcomingCount = computed(() => this.appointments().filter(a => this.isUpcoming(a)).length);
  readonly pastCount     = computed(() => this.appointments().filter(a => !this.isUpcoming(a)).length);
  readonly filtered      = computed(() => {
    const tab = this.activeTab();
    const all = this.appointments();
    if (tab === 'upcoming') return all.filter(a => this.isUpcoming(a));
    if (tab === 'past')     return all.filter(a => !this.isUpcoming(a));
    return all;
  });

  constructor(private svc: AppointmentService, private router: Router) {}

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.loading.set(true);
    this.svc.getMyAppointments().subscribe({
      next: list => { this.appointments.set(list); this.loading.set(false); },
      error: ()  => this.loading.set(false)
    });
  }

  private isUpcoming(a: Appointment): boolean {
    return new Date(a.slotTime) >= new Date() && a.status !== 'Cancelled' && a.status !== 'NoShow';
  }

  isCancellable(a: Appointment): boolean {
    const apptMs   = new Date(a.slotTime).getTime();
    const twoHours = 2 * 60 * 60 * 1000;
    return apptMs - Date.now() > twoHours;
  }

  apptEnd(a: Appointment): Date {
    const d = new Date(a.slotTime);
    d.setMinutes(d.getMinutes() + 30);
    return d;
  }

  badgeClass(status: string): Record<string, boolean> {
    return {
      'badge--confirmed':  status === 'Scheduled',
      'badge--arrived':    status === 'Arrived',
      'badge--completed':  status === 'Completed',
      'badge--cancelled':  status === 'Cancelled',
      'badge--noshow':     status === 'NoShow'
    };
  }

  // ── Cancel ─────────────────────────────────────────────────────────────────

  openCancel(appt: Appointment): void { this.cancelTarget.set(appt); }

  confirmCancel(): void {
    const appt = this.cancelTarget();
    if (!appt) return;
    this.busy.set(appt.id);
    this.svc.cancelAppointment(appt.id).subscribe({
      next: res => {
        this.busy.set(null);
        this.cancelTarget.set(null);
        this.showToast(res.message || 'Appointment cancelled.');
        this.refresh();
      },
      error: err => {
        this.busy.set(null);
        this.showToast(err?.error?.error ?? 'Cancellation failed. Please try again.');
      }
    });
  }

  // ── Reschedule ──────────────────────────────────────────────────────────────

  openReschedule(appt: Appointment): void {
    this.rescheduleTarget.set(appt);
    this.rSlots.set([]);
    this.rSelected.set(null);
    this.rDateControl.setValue(this.today);
  }

  loadRescheduleSlots(): void {
    const date = this.rDateControl.value;
    if (!date) return;
    this.rLoading.set(true);
    this.svc.getSlots(date).subscribe({
      next: slots => { this.rSlots.set(slots.filter(s => s.isAvailable)); this.rLoading.set(false); },
      error: ()   => { this.rSlots.set([]); this.rLoading.set(false); }
    });
  }

  confirmReschedule(appt: Appointment): void {
    const slot = this.rSelected();
    if (!slot) return;
    this.busy.set(appt.id);
    this.svc.rescheduleAppointment(appt.id, { newSlotId: slot.id }).subscribe({
      next: () => {
        this.busy.set(null);
        this.rescheduleTarget.set(null);
        this.showToast('Appointment rescheduled successfully.');
        this.refresh();
      },
      error: err => {
        this.busy.set(null);
        this.showToast(err?.error?.error ?? 'Reschedule failed. Please try again.');
      }
    });
  }

  private showToast(msg: string): void {
    this.toast.set(msg);
    setTimeout(() => this.toast.set(''), 4000);
  }
}
