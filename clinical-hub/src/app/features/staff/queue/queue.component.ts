import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AppointmentService, QueueEntry } from '../../../core/services/appointment.service';

@Component({
  selector: 'app-staff-queue',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  template: `
<div class="page-header">
  <h1>Same-day queue — {{ today }}</h1>
  <a routerLink="/staff/walkin" class="btn btn--primary">+ Add walk-in</a>
</div>

<!-- Status bar -->
@if (!loading()) {
  <div class="queue-status" role="region" aria-label="Queue summary">
    <div class="queue-stat">
      <p class="queue-stat__num">{{ activeEntries().length }}</p>
      <p class="queue-stat__label">Total in queue</p>
    </div>
    <div class="divider"></div>
    <div class="queue-stat">
      <p class="queue-stat__num">{{ inProgressCount() }}</p>
      <p class="queue-stat__label">In progress</p>
    </div>
    <div class="divider"></div>
    <div class="queue-stat">
      <p class="queue-stat__num" [style.color]="walkInCount() > 0 ? 'var(--cw)' : ''">{{ walkInCount() }}</p>
      <p class="queue-stat__label">Walk-ins</p>
    </div>
    <div class="divider"></div>
    <div class="queue-stat">
      <p class="queue-stat__num">~{{ avgWait() }} min</p>
      <p class="queue-stat__label">Est. wait per patient</p>
    </div>
  </div>
  <p style="font-size:12px;color:var(--ct2);margin-bottom:12px;">
    Enter a position number to reorder. Changes are saved automatically.
  </p>
}

<!-- Loading -->
@if (loading()) {
  <p class="page-sub" style="padding:32px 0;" aria-live="polite">Loading queue…</p>
}

<!-- Empty -->
@if (!loading() && activeEntries().length === 0) {
  <div class="empty" role="status">
    <svg width="36" height="36" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M3 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1z" clip-rule="evenodd"/></svg>
    <h3>Queue is empty</h3>
    <p>No patients in today's queue. <a routerLink="/staff/walkin">Add a walk-in</a> to get started.</p>
  </div>
}

<!-- Queue table -->
@if (!loading() && activeEntries().length > 0) {
  <div class="queue-table" role="table" aria-label="Same-day queue">
    <div class="queue-table-header" role="row">
      <span role="columnheader">Pos.</span>
      <span role="columnheader">Patient</span>
      <span role="columnheader">Type</span>
      <span role="columnheader">Arrived</span>
      <span role="columnheader">Wait</span>
      <span role="columnheader">Status</span>
      <span role="columnheader">Actions</span>
    </div>
    @for (e of activeEntries(); track e.id) {
      <div class="queue-row" role="row">
        <span role="cell">
          <input class="pos-input" type="number" [value]="e.position" min="1"
                 (change)="reposition(e, $event)"
                 [attr.aria-label]="'Position for ' + e.patientName" />
        </span>
        <div role="cell">
          <a class="patient-link" [routerLink]="['/staff/patients', e.patientId, 'view360']"
             [attr.aria-label]="'View 360 profile for ' + e.patientName">{{ e.patientName }}</a>
          <p class="chief-complaint">{{ e.chiefComplaint }}</p>
        </div>
        <span role="cell">
          <span class="badge" [class.badge--walkin]="e.arrivalType === 'WalkIn'"
                              [class.badge--appt]="e.arrivalType === 'Appointment'">
            {{ e.arrivalType === 'WalkIn' ? 'Walk-in' : 'Appointment' }}
          </span>
        </span>
        <span role="cell" class="time-cell">{{ e.arrivedAt | date:'h:mm a' }}</span>
        <span role="cell" class="wait-cell">{{ e.waitMinutes }}m</span>
        <span role="cell">
          <span class="badge" [class.badge--waiting]="e.status === 'Waiting'"
                              [class.badge--inprogress]="e.status === 'InProgress'"
                              [class.badge--completed]="e.status === 'Completed'">
            {{ e.status === 'InProgress' ? 'In progress' : e.status }}
          </span>
        </span>
        <div role="cell" class="row-actions">
          @if (e.status === 'Waiting') {
            <button class="btn btn--primary btn--sm" (click)="updateStatus(e, 'InProgress')"
                    [disabled]="busy() === e.id"
                    [attr.aria-label]="'Start seeing ' + e.patientName">See</button>
          }
          @if (e.status === 'InProgress') {
            <button class="btn btn--sm btn--secondary" (click)="updateStatus(e, 'Completed')"
                    [disabled]="busy() === e.id"
                    [attr.aria-label]="'Complete ' + e.patientName">Done</button>
          }
          <button class="btn btn--sm btn--danger" (click)="remove(e)"
                  [disabled]="busy() === e.id"
                  [attr.aria-label]="'Remove ' + e.patientName + ' from queue'">✕</button>
        </div>
      </div>
    }
  </div>
}

<!-- Toast -->
@if (toast()) {
  <div class="toast show" role="status" aria-live="polite">{{ toast() }}</div>
}
  `,
  styles: [`
    :root{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--cw:#D4820A;--cwb:#FEF5E7;--ci:#1C6EA4;--cib:#E8F0F8;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
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
    .btn--sm{padding:4px 10px;font-size:12px;}
    .queue-status{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:14px 20px;margin-bottom:12px;display:flex;align-items:center;gap:0;flex-wrap:wrap;}
    .queue-stat{text-align:center;padding:0 24px;}
    .queue-stat__num{font-size:24px;font-weight:700;color:var(--cp);}
    .queue-stat__label{font-size:12px;color:var(--ct2);}
    .divider{width:1px;height:40px;background:var(--cb);}
    .queue-table{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);overflow:hidden;}
    .queue-table-header{display:grid;grid-template-columns:60px 1fr 120px 100px 70px 120px 140px;padding:10px 16px;border-bottom:2px solid var(--cb);font-size:12px;font-weight:600;color:var(--ct2);text-transform:uppercase;letter-spacing:.04em;}
    .queue-row{display:grid;grid-template-columns:60px 1fr 120px 100px 70px 120px 140px;padding:14px 16px;border-bottom:1px solid var(--cs2);align-items:center;}
    .queue-row:last-child{border-bottom:none;}
    .queue-row:hover{background:var(--cs1);}
    .pos-input{width:48px;padding:4px 8px;border:1px solid var(--cb);border-radius:var(--r2);font-size:13px;text-align:center;}
    .pos-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .patient-link{color:var(--cp);text-decoration:none;font-weight:500;font-size:13px;}
    .patient-link:hover{text-decoration:underline;}
    .chief-complaint{font-size:12px;color:var(--ct2);margin:2px 0 0;}
    .badge{display:inline-flex;align-items:center;gap:4px;padding:2px 8px;border-radius:var(--r2);font-size:12px;font-weight:500;}
    .badge--walkin{background:var(--cwb);color:var(--cw);}
    .badge--appt{background:var(--cib);color:var(--ci);}
    .badge--waiting{background:var(--cs2);color:var(--ct2);}
    .badge--inprogress{background:var(--cokb);color:var(--cok);}
    .badge--completed{background:var(--cs2);color:var(--ctd);}
    .time-cell,.wait-cell{font-size:13px;color:var(--ct2);}
    .row-actions{display:flex;gap:6px;}
    .empty{padding:64px 32px;text-align:center;color:var(--ct2);}
    .empty svg{margin:0 auto 12px;display:block;color:var(--ctd);}
    .empty h3{font-size:16px;font-weight:600;margin-bottom:8px;}
    .empty a{color:var(--cp);}
    .page-sub{font-size:14px;color:var(--ct2);}
    .toast{position:fixed;bottom:80px;left:50%;transform:translateX(-50%);background:var(--ct1);color:#fff;padding:12px 20px;border-radius:var(--r2);font-size:13px;z-index:600;display:none;}
    .toast.show{display:block;}
  `]
})
export class QueueComponent implements OnInit {
  entries   = signal<QueueEntry[]>([]);
  loading   = signal(true);
  busy      = signal<number | null>(null);
  toast     = signal('');

  readonly today          = new Date().toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  readonly activeEntries  = computed(() => this.entries().filter(e => e.status !== 'Removed' && e.status !== 'Completed').sort((a, b) => a.position - b.position));
  readonly inProgressCount = computed(() => this.entries().filter(e => e.status === 'InProgress').length);
  readonly walkInCount    = computed(() => this.entries().filter(e => e.arrivalType === 'WalkIn').length);
  readonly avgWait        = computed(() => {
    const w = this.entries().filter(e => e.status === 'Waiting');
    if (!w.length) return 0;
    return Math.round(w.reduce((s, e) => s + e.waitMinutes, 0) / w.length);
  });

  constructor(private svc: AppointmentService) {}

  ngOnInit(): void { this.loadQueue(); }

  loadQueue(): void {
    this.loading.set(true);
    this.svc.getQueue().subscribe({
      next: d => { this.entries.set(d); this.loading.set(false); },
      error: () => { this.entries.set([]); this.loading.set(false); }
    });
  }

  reposition(entry: QueueEntry, event: Event): void {
    const newPos = Number((event.target as HTMLInputElement).value);
    if (newPos < 1) return;
    this.svc.reorderQueue([{ id: entry.id, position: newPos }]).subscribe({
      next: () => {
        this.entries.update(list => {
          const updated = list.find(e => e.id === entry.id);
          if (updated) updated.position = newPos;
          return [...list];
        });
      },
      error: err => this.showToast(err?.error?.error ?? 'Reorder failed.')
    });
  }

  updateStatus(entry: QueueEntry, status: QueueEntry['status']): void {
    this.busy.set(entry.id);
    this.svc.updateQueueStatus(entry.id, status).subscribe({
      next: () => {
        this.entries.update(list => {
          const e = list.find(x => x.id === entry.id);
          if (e) e.status = status;
          return [...list];
        });
        this.busy.set(null);
      },
      error: err => { this.busy.set(null); this.showToast(err?.error?.error ?? 'Update failed.'); }
    });
  }

  remove(entry: QueueEntry): void {
    this.busy.set(entry.id);
    this.svc.removeFromQueue(entry.id).subscribe({
      next: () => {
        this.entries.update(list => list.filter(e => e.id !== entry.id));
        this.busy.set(null);
        this.showToast(`${entry.patientName} removed from queue.`);
      },
      error: err => { this.busy.set(null); this.showToast(err?.error?.error ?? 'Remove failed.'); }
    });
  }

  private showToast(msg: string): void {
    this.toast.set(msg);
    setTimeout(() => this.toast.set(''), 3500);
  }
}
