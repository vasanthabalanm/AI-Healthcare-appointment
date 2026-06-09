import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { AppointmentService, Slot } from '../../../core/services/appointment.service';

interface CalDay {
  date: Date;
  inMonth: boolean;
  isPast: boolean;
  isToday: boolean;
  hasSlots: boolean;
  isFullyBooked: boolean;
}

@Component({
  selector: 'app-patient-book',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
<h1 class="page-title">Book appointment</h1>

<div class="cal-layout">
  <!-- ── Calendar card ───────────────────────────────────────────── -->
  <div class="card cal-card">
    <div class="cal-nav">
      <button class="cal-nav-btn" (click)="prevMonth()" aria-label="Previous month">&#8592;</button>
      <span class="cal-month-label">{{ monthLabel }}</span>
      <button class="cal-nav-btn" (click)="nextMonth()" aria-label="Next month">&#8594;</button>
    </div>

    <div class="cal-weekdays" aria-hidden="true">
      <span *ngFor="let d of weekDays">{{ d }}</span>
    </div>

    <div class="cal-grid" role="grid" [attr.aria-label]="monthLabel + ' calendar'">
      <button
        *ngFor="let day of calDays()"
        class="cal-day"
        [class.cal-day--other]="!day.inMonth"
        [class.cal-day--past]="day.isPast"
        [class.cal-day--today]="day.isToday"
        [class.cal-day--available]="day.inMonth && !day.isPast && day.hasSlots && !day.isFullyBooked"
        [class.cal-day--booked]="day.inMonth && !day.isPast && day.isFullyBooked"
        [class.cal-day--selected]="isSameDay(day.date, selectedDate())"
        [disabled]="!day.inMonth || day.isPast || (!day.hasSlots && !loadingSlots())"
        (click)="selectDate(day)"
        role="gridcell"
        [attr.aria-label]="dayAriaLabel(day)"
        [attr.aria-selected]="isSameDay(day.date, selectedDate())"
        [attr.aria-disabled]="!day.inMonth || day.isPast"
      >
        <span class="cal-day-num">{{ day.date.getDate() }}</span>
        <span class="cal-dot" *ngIf="day.inMonth && !day.isPast && day.hasSlots && !day.isFullyBooked" aria-hidden="true"></span>
      </button>
    </div>

    <!-- Legend -->
    <div class="cal-legend" aria-label="Calendar legend">
      <span class="legend-item"><span class="legend-dot legend-dot--avail"></span>Available</span>
      <span class="legend-item"><span class="legend-dot legend-dot--full"></span>Fully booked</span>
      <span class="legend-item"><span class="legend-dot legend-dot--past"></span>Past</span>
    </div>
  </div>

  <!-- ── Slot panel ──────────────────────────────────────────────── -->
  <div class="slot-panel">
    <ng-container *ngIf="!selectedDate(); else slotContent">
      <div class="slot-empty">
        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true" style="opacity:.3;margin-bottom:12px">
          <rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>
        </svg>
        <p>Select a date to see available slots</p>
      </div>
    </ng-container>

    <ng-template #slotContent>
      <div class="slot-date-header">
        <strong>{{ selectedDate() | date:'EEEE, d MMMM y' }}</strong>
      </div>

      <div *ngIf="loadingSlots()" class="slot-loading" role="status" aria-live="polite">Loading slots…</div>

      <div *ngIf="!loadingSlots() && slots().length === 0" class="slot-empty" role="status">
        No available slots on this date.
      </div>

      <div class="slot-list" role="listbox" aria-label="Available time slots" *ngIf="!loadingSlots() && slots().length > 0">
        <button
          *ngFor="let slot of slots()"
          class="slot-btn"
          [class.slot-btn--selected]="selected()?.id === slot.id"
          role="option"
          [attr.aria-selected]="selected()?.id === slot.id"
          (click)="selectSlot(slot)"
        >
          <span class="slot-time">{{ slot.slotTime | date:'h:mm a' }}</span>
          <span class="slot-badge slot-badge--avail">Available</span>
        </button>
      </div>

      <!-- Booking summary -->
      <div class="booking-summary" *ngIf="selected()">
        <h3 class="booking-summary__title">Booking summary</h3>
        <div class="booking-summary__row">
          <span class="booking-summary__label">Date</span>
          <span>{{ selectedDate() | date:'d MMM y' }}</span>
        </div>
        <div class="booking-summary__row">
          <span class="booking-summary__label">Time</span>
          <span>{{ selected()!.slotTime | date:'h:mm a' }}</span>
        </div>
        <div class="booking-summary__row">
          <span class="booking-summary__label">Duration</span>
          <span>{{ selected()!.durationMinutes }} min</span>
        </div>
      </div>

      <div class="slot-actions" *ngIf="!loadingSlots()">
        <div *ngIf="error()" class="error-msg" role="alert">{{ error() }}</div>
        <div *ngIf="successMsg()" class="success-msg" role="status">{{ successMsg() }}</div>
        <button class="btn btn--primary btn--full" [disabled]="!selected() || submitting()"
                (click)="confirmBooking()">
          {{ submitting() ? 'Booking…' : 'Confirm booking' }}
        </button>
        <a routerLink="/patient/waitlist" class="waitlist-link">Join waitlist for a different slot</a>
      </div>
    </ng-template>
  </div>
</div>
  `,
  styles: [`
    :host{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--cok:#1A7A4A;--cokb:#E8F5EE;--r2:4px;--r3:8px;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;}
    *{box-sizing:border-box;}
    .page-title{font-size:24px;font-weight:600;margin:0 0 24px;font-family:var(--ff);}
    .card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);}
    .cal-layout{display:grid;grid-template-columns:340px 1fr;gap:24px;align-items:start;font-family:var(--ff);}
    /* Calendar */
    .cal-card{padding:20px;}
    .cal-nav{display:flex;align-items:center;justify-content:space-between;margin-bottom:16px;}
    .cal-nav-btn{background:none;border:1px solid var(--cb);border-radius:var(--r2);width:32px;height:32px;cursor:pointer;font-size:16px;color:var(--ct1);display:flex;align-items:center;justify-content:center;}
    .cal-nav-btn:hover{background:var(--cs1);}
    .cal-month-label{font-size:15px;font-weight:600;color:var(--ct1);}
    .cal-weekdays{display:grid;grid-template-columns:repeat(7,1fr);text-align:center;font-size:11px;font-weight:600;color:var(--ctd);margin-bottom:6px;}
    .cal-grid{display:grid;grid-template-columns:repeat(7,1fr);gap:2px;}
    .cal-day{background:none;border:none;border-radius:var(--r2);padding:6px 0;cursor:pointer;display:flex;flex-direction:column;align-items:center;gap:3px;font-size:13px;color:var(--ct1);position:relative;}
    .cal-day:hover:not([disabled]):not(.cal-day--selected){background:var(--cs1);}
    .cal-day[disabled]{cursor:default;}
    .cal-day-num{line-height:1;}
    .cal-day--other .cal-day-num{color:var(--ctd);}
    .cal-day--past .cal-day-num{color:var(--ctd);}
    .cal-day--today{font-weight:600;}
    .cal-day--today .cal-day-num{text-decoration:underline;color:var(--cp);}
    .cal-day--available:not(.cal-day--selected){background:var(--cps);}
    .cal-day--booked .cal-day-num{color:var(--ctd);}
    .cal-day--selected{background:var(--cp)!important;border-radius:var(--r2);}
    .cal-day--selected .cal-day-num{color:#fff;font-weight:600;}
    .cal-day--selected .cal-dot{background:#fff!important;}
    .cal-dot{width:5px;height:5px;border-radius:50%;background:var(--cp);}
    .cal-legend{display:flex;gap:16px;margin-top:16px;padding-top:12px;border-top:1px solid var(--cs2);}
    .legend-item{display:flex;align-items:center;gap:5px;font-size:12px;color:var(--ct2);}
    .legend-dot{width:8px;height:8px;border-radius:50%;}
    .legend-dot--avail{background:var(--cp);}
    .legend-dot--full{background:var(--ctd);}
    .legend-dot--past{background:var(--cs2);border:1px solid var(--cb);}
    /* Slot panel */
    .slot-panel{display:flex;flex-direction:column;gap:16px;}
    .slot-date-header{font-size:15px;font-weight:600;color:var(--ct1);padding:16px 20px;background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3) var(--r3) 0 0;border-bottom:1px solid var(--cs2);}
    .slot-loading,.slot-empty{padding:32px;text-align:center;font-size:14px;color:var(--ct2);background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);display:flex;flex-direction:column;align-items:center;}
    .slot-list{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);overflow:hidden;}
    .slot-btn{width:100%;display:flex;align-items:center;justify-content:space-between;padding:14px 20px;border:none;border-bottom:1px solid var(--cs2);background:var(--cs0);cursor:pointer;font-size:14px;font-family:var(--ff);}
    .slot-btn:last-child{border-bottom:none;}
    .slot-btn:hover:not(.slot-btn--selected){background:var(--cs1);}
    .slot-btn--selected{background:var(--cps);border-left:3px solid var(--cp);}
    .slot-time{font-weight:500;color:var(--ct1);}
    .slot-badge{font-size:11px;padding:2px 8px;border-radius:10px;font-weight:500;}
    .slot-badge--avail{background:var(--cokb);color:var(--cok);}
    .booking-summary{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);padding:16px 20px;}
    .booking-summary__title{font-size:13px;font-weight:600;margin:0 0 12px;color:var(--ct1);}
    .booking-summary__row{display:flex;justify-content:space-between;font-size:14px;margin-bottom:8px;color:var(--ct1);}
    .booking-summary__row:last-child{margin-bottom:0;}
    .booking-summary__label{color:var(--ct2);}
    .slot-actions{display:flex;flex-direction:column;gap:10px;}
    .btn{display:inline-flex;align-items:center;justify-content:center;padding:10px 24px;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;border:none;font-family:var(--ff);}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not([disabled]){background:var(--cph);}
    .btn--primary[disabled]{opacity:.5;cursor:not-allowed;}
    .btn--full{width:100%;}
    .waitlist-link{font-size:13px;color:var(--cp);text-decoration:none;text-align:center;}
    .waitlist-link:hover{text-decoration:underline;}
    .error-msg{font-size:13px;color:var(--ce);padding:8px 12px;background:#fdecea;border-radius:var(--r2);}
    .success-msg{font-size:13px;color:var(--cok);padding:8px 12px;background:var(--cokb);border-radius:var(--r2);}
    @media(max-width:900px){.cal-layout{grid-template-columns:1fr;}}
  `]
})
export class BookComponent implements OnInit {
  readonly weekDays = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];

  private viewYear  = signal(new Date().getFullYear());
  private viewMonth = signal(new Date().getMonth()); // 0-based

  selectedDate = signal<Date | null>(null);
  slots        = signal<Slot[]>([]);
  selected     = signal<Slot | null>(null);
  loadingSlots = signal(false);
  submitting   = signal(false);
  error        = signal('');
  successMsg   = signal('');

  // days keyed by YYYY-MM-DD: available = has ≥1 open slot; fullyBooked = has slots but none open
  private availableDays   = signal<Set<string>>(new Set());
  private fullyBookedDays = signal<Set<string>>(new Set());

  get monthLabel(): string {
    return new Date(this.viewYear(), this.viewMonth(), 1)
      .toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
  }

  calDays = computed<CalDay[]>(() => {
    const year  = this.viewYear();
    const month = this.viewMonth();
    const today = new Date(); today.setHours(0, 0, 0, 0);

    const firstDay = new Date(year, month, 1).getDay();   // 0=Sun
    const daysInMonth = new Date(year, month + 1, 0).getDate();

    const days: CalDay[] = [];

    // Pad leading days from previous month
    const prevMonth  = new Date(year, month, 0);
    const prevDays   = prevMonth.getDate();
    for (let i = firstDay - 1; i >= 0; i--) {
      const d = new Date(year, month - 1, prevDays - i);
      days.push({ date: d, inMonth: false, isPast: true, isToday: false, hasSlots: false, isFullyBooked: false });
    }

    // Current month days
    for (let d = 1; d <= daysInMonth; d++) {
      const date = new Date(year, month, d);
      const key  = this.toDateKey(date);
      const isPast = date < today;
      days.push({
        date,
        inMonth: true,
        isPast,
        isToday: this.isSameDay(date, today),
        hasSlots: this.availableDays().has(key),
        isFullyBooked: !isPast && this.fullyBookedDays().has(key),
      });
    }

    // Pad trailing days
    const remaining = 42 - days.length;
    for (let d = 1; d <= remaining; d++) {
      days.push({ date: new Date(year, month + 1, d), inMonth: false, isPast: false, isToday: false, hasSlots: false, isFullyBooked: false });
    }
    return days;
  });

  constructor(private svc: AppointmentService, private router: Router) {}

  ngOnInit(): void {
    // Pre-load slots for today so the calendar shows dots on first render.
    this.prefetchMonth();
    // Auto-select today and if it has no future slots, advance to tomorrow.
    const today = new Date();
    this.selectedDate.set(today);
    this.loadSlots(today, /* autoAdvance */ true);
  }

  prevMonth(): void {
    const m = this.viewMonth() - 1;
    if (m < 0) { this.viewMonth.set(11); this.viewYear.set(this.viewYear() - 1); }
    else        { this.viewMonth.set(m); }
    this.prefetchMonth();
  }

  nextMonth(): void {
    const m = this.viewMonth() + 1;
    if (m > 11) { this.viewMonth.set(0); this.viewYear.set(this.viewYear() + 1); }
    else         { this.viewMonth.set(m); }
    this.prefetchMonth();
  }

  selectDate(day: CalDay): void {
    if (!day.inMonth || day.isPast) return;
    this.selectedDate.set(day.date);
    this.selected.set(null);
    this.error.set('');
    this.successMsg.set('');
    this.loadSlots(day.date);
  }

  selectSlot(slot: Slot): void {
    this.selected.set(slot);
    this.error.set('');
  }

  confirmBooking(): void {
    const slot = this.selected();
    if (!slot) return;
    this.submitting.set(true);
    this.error.set('');
    this.svc.bookAppointment({ slotId: slot.id }).subscribe({
      next: res => {
        this.submitting.set(false);
        this.successMsg.set(`Appointment booked!`);
        setTimeout(() => this.router.navigate(['/patient/appointments']), 1500);
      },
      error: err => {
        this.submitting.set(false);
        this.error.set(err?.error?.error ?? 'Booking failed. Please try again.');
      }
    });
  }

  isSameDay(a: Date | null, b: Date | null): boolean {
    if (!a || !b) return false;
    return a.getFullYear() === b.getFullYear()
        && a.getMonth()    === b.getMonth()
        && a.getDate()     === b.getDate();
  }

  dayAriaLabel(day: CalDay): string {
    const label = day.date.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' });
    if (!day.inMonth || day.isPast) return label + ' (unavailable)';
    if (day.hasSlots) return label + ' (available)';
    if (day.isFullyBooked) return label + ' (fully booked)';
    return label;
  }

  private loadSlots(date: Date, autoAdvance = false): void {
    this.loadingSlots.set(true);
    this.slots.set([]);
    this.svc.getSlots(this.toDateKey(date)).subscribe({
      next: slots => {
        if (autoAdvance && slots.length === 0) {
          // Today has no future slots — jump to tomorrow automatically.
          const tomorrow = new Date(date);
          tomorrow.setDate(tomorrow.getDate() + 1);
          this.selectedDate.set(tomorrow);
          this.loadSlots(tomorrow);
        } else {
          this.slots.set(slots);
        }
        this.loadingSlots.set(false);
      },
      error: () => { this.loadingSlots.set(false); }
    });
  }

  private prefetchMonth(): void {
    // Fetch all slots (available + unavailable) for the next 7 days of the visible month.
    // This lets the calendar distinguish: available (green) / fully booked (grey) / no slots.
    const year  = this.viewYear();
    const month = this.viewMonth();
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const daysInMonth = new Date(year, month + 1, 0).getDate();

    const available   = new Set<string>(this.availableDays());
    const fullyBooked = new Set<string>(this.fullyBookedDays());

    const start = new Date(Math.max(new Date(year, month, 1).getTime(), today.getTime()));
    const checks: Promise<void>[] = [];
    for (let i = 0; i < Math.min(7, daysInMonth); i++) {
      const d = new Date(start);
      d.setDate(d.getDate() + i);
      if (d.getMonth() !== month) break;
      const key = this.toDateKey(d);
      const p = this.svc.getAllSlots(key).toPromise()
        .then(slots => {
          if (!slots || slots.length === 0) return;
          const hasAny       = slots.length > 0;
          const hasAvailable = slots.some(s => s.isAvailable === true);
          if (hasAvailable) {
            available.add(key);
            fullyBooked.delete(key);
          } else if (hasAny) {
            // All slots exist but none is available → fully booked
            fullyBooked.add(key);
            available.delete(key);
          }
        })
        .catch(() => {});
      checks.push(p);
    }
    Promise.all(checks).then(() => {
      this.availableDays.set(available);
      this.fullyBookedDays.set(fullyBooked);
    });
  }

  private toDateKey(d: Date): string {
    return d.toISOString().split('T')[0];
  }
}

