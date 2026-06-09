import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AppointmentService, Slot } from '../../../core/services/appointment.service';

@Component({
  selector: 'app-patient-waitlist',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  template: `
<div class="page-header">
  <div>
    <h1>Waitlist</h1>
    <p class="page-sub">Join a waitlist to be automatically offered a slot when one becomes available.</p>
  </div>
  <a routerLink="/patient/book" class="btn btn--secondary">← Back to booking</a>
</div>

<div class="card" style="max-width:560px">

  <!-- Date picker -->
  <div class="field">
    <label class="form-label" for="wDate">Preferred date <span class="required" aria-hidden="true">*</span></label>
    <input
      id="wDate"
      type="date"
      class="form-input"
      [formControl]="dateControl"
      [min]="today"
      (change)="loadSlots()"
    />
  </div>

  <!-- Slot list -->
  <div *ngIf="loading()" class="slot-loading" role="status">Loading slots…</div>

  <div *ngIf="!loading() && dateControl.value && slots().length === 0" class="slot-empty" role="status">
    No slots found for this date.
  </div>

  <ul *ngIf="!loading() && slots().length > 0"
      class="slot-list" role="listbox" aria-label="Slots to join waitlist">
    <li
      *ngFor="let slot of slots()"
      class="slot-item"
      [class.slot-item--selected]="selected()?.id === slot.id"
      role="option"
      [attr.aria-selected]="selected()?.id === slot.id"
      (click)="selected.set(slot)"
      (keydown.enter)="selected.set(slot)"
      tabindex="0"
    >
      <span class="slot-time">{{ slot.slotTime | date:'h:mm a' }}</span>
      <span class="slot-duration">{{ slot.durationMinutes }} min</span>
      <span class="badge badge--orange">Unavailable</span>
    </li>
  </ul>

  <!-- Hint -->
  <p class="hint" *ngIf="selected()">
    You will be notified when a slot near <strong>{{ selected()!.slotTime | date:'h:mm a, d MMM' }}</strong> becomes available.
  </p>

  <!-- Error / success -->
  <p *ngIf="error()" class="error-msg" role="alert">{{ error() }}</p>
  <p *ngIf="success()" class="success-msg" role="status">{{ success() }}</p>

  <div class="actions">
    <button
      class="btn btn--primary"
      [disabled]="!selected() || submitting()"
      (click)="join()"
    >
      {{ submitting() ? 'Joining…' : 'Join Waitlist' }}
    </button>
  </div>
</div>
  `,
  styles: [`
    :host{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--cow:#D4820A;--cowb:#FEF3CD;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .page-header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:24px;}
    h1{font-size:24px;font-weight:600;margin:0 0 4px;font-family:var(--ff);}
    .page-sub{font-size:14px;color:var(--ct2);margin:0;}
    .card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);padding:24px;}
    .field{margin-bottom:20px;}
    .form-label{display:block;font-size:13px;font-weight:500;margin-bottom:6px;color:var(--ct1);font-family:var(--ff);}
    .required{color:var(--ce);}
    .form-input{width:100%;padding:8px 12px;border:1px solid var(--cb);border-radius:var(--r2);font-size:14px;font-family:var(--ff);background:var(--cs0);}
    .form-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .slot-loading,.slot-empty{padding:20px 0;font-size:14px;color:var(--ctd);text-align:center;}
    .slot-list{list-style:none;padding:0;margin:0 0 16px;border:1px solid var(--cb);border-radius:var(--r2);overflow:hidden;}
    .slot-item{display:flex;align-items:center;gap:12px;padding:12px 16px;border-bottom:1px solid var(--cs2);cursor:pointer;background:var(--cs0);font-family:var(--ff);font-size:14px;}
    .slot-item:last-child{border-bottom:none;}
    .slot-item:hover{background:var(--cs1);}
    .slot-item--selected{background:var(--cps);border-left:3px solid var(--cp);}
    .slot-time{font-weight:500;color:var(--ct1);}
    .slot-duration{font-size:12px;color:var(--ctd);margin-left:auto;}
    .badge{font-size:11px;padding:2px 8px;border-radius:10px;font-weight:500;}
    .badge--orange{background:var(--cowb);color:var(--cow);}
    .hint{font-size:13px;color:var(--ct2);margin:0 0 16px;padding:10px 14px;background:var(--cs1);border-radius:var(--r2);}
    .error-msg{font-size:13px;color:var(--ce);padding:8px 12px;background:var(--ceb);border-radius:var(--r2);margin:0 0 12px;}
    .success-msg{font-size:13px;color:var(--cok);padding:8px 12px;background:var(--cokb);border-radius:var(--r2);margin:0 0 12px;}
    .actions{display:flex;justify-content:flex-end;margin-top:16px;}
    .btn{display:inline-flex;align-items:center;padding:9px 20px;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;border:1px solid transparent;font-family:var(--ff);text-decoration:none;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not([disabled]){background:var(--cph);}
    .btn--primary[disabled]{opacity:.5;cursor:not-allowed;}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover{background:var(--cs1);}
  `]
})
export class WaitlistComponent implements OnInit {
  readonly today = new Date().toISOString().split('T')[0];

  dateControl = new FormControl(this.today, Validators.required);

  slots      = signal<Slot[]>([]);
  selected   = signal<Slot | null>(null);
  loading    = signal(false);
  submitting = signal(false);
  error      = signal('');
  success    = signal('');

  constructor(private svc: AppointmentService) {}

  ngOnInit(): void {
    this.loadSlots();
  }

  loadSlots(): void {
    const date = this.dateControl.value;
    if (!date) { return; }
    this.loading.set(true);
    this.error.set('');
    this.selected.set(null);
    this.svc.getSlots(date).subscribe({
      next: slots => { this.slots.set(slots); this.loading.set(false); },
      error: ()   => { this.error.set('Failed to load slots.'); this.loading.set(false); }
    });
  }

  join(): void {
    const slot = this.selected();
    const date = this.dateControl.value;
    if (!slot || !date) { return; }
    this.submitting.set(true);
    this.error.set('');
    this.svc.joinWaitlist({ preferredSlotId: slot.id, preferredSlotDate: date }).subscribe({
      next: res => {
        this.submitting.set(false);
        this.success.set(res.message || `Joined waitlist (ID: ${res.waitlistEntryId}).`);
        this.selected.set(null);
      },
      error: err => {
        this.submitting.set(false);
        this.error.set(err?.error?.error ?? 'Failed to join waitlist.');
      }
    });
  }
}
