import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AppointmentService, QueueEntry } from '../../../core/services/appointment.service';

interface PatientResult {
  id: number;
  fullName: string;
  dob: string | null;
  email: string;
}

@Component({
  selector: 'app-staff-walkin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
<a routerLink="/staff/schedule" class="back-link">← Back to schedule</a>
<h1>Walk-in registration</h1>
<p class="page-sub">Search for an existing patient or create a minimal profile for a new walk-in.</p>

<div class="layout">
  <!-- Left column -->
  <div>
    <!-- Step 1: Search -->
    <div class="search-card" role="search" aria-label="Patient search">
      <h2>Search patient</h2>
      <div class="search-row">
        <input class="search-input" type="search" [(ngModel)]="searchQuery"
               placeholder="Name, email or date of birth…"
               (keydown.enter)="searchPatient()"
               aria-label="Search by name, date of birth, or email" />
        <button class="btn btn--primary" (click)="searchPatient()"
                [disabled]="!searchQuery.trim() || searching">
          {{ searching ? '…' : 'Search' }}
        </button>
      </div>

      <!-- Search result found -->
      @if (foundPatient && !selectedPatient) {
        <div class="result-card" role="status" aria-live="polite" aria-label="Patient found">
          <div class="result-info">
            <strong>{{ foundPatient.fullName }}</strong>
            <p>{{ foundPatient.dob ? 'DOB: ' + formatDate(foundPatient.dob) + ' · ' : '' }}{{ foundPatient.email }}</p>
          </div>
          <button class="btn btn--primary" (click)="selectPatient()"
                  [attr.aria-label]="'Add ' + foundPatient.fullName + ' to queue'">
            Add to queue
          </button>
        </div>
      }

      <!-- Not found -->
      @if (searched && !foundPatient) {
        <div class="not-found">
          <p>No patient found for "{{ searchQuery }}". Create a minimal profile for this walk-in.</p>
          <button class="btn btn--secondary" (click)="useNewForm()">Create minimal profile</button>
        </div>
      }
    </div>

    <!-- Step 2: Form (existing or new patient) -->
    @if (showForm) {
      <div class="minimal-form" role="form" aria-label="Walk-in details form">
        <h2>{{ selectedPatient ? 'Confirm and add to queue' : 'New walk-in patient' }}</h2>

        @if (selectedPatient) {
          <div class="selected-info">
            <strong>{{ selectedPatient.fullName }}</strong>
            <p>{{ selectedPatient.dob ? 'DOB: ' + formatDate(selectedPatient.dob) + ' · ' : '' }}{{ selectedPatient.email }}</p>
          </div>
        } @else {
          <div class="field-row">
            <div class="field">
              <label for="wfn">First name *</label>
              <input id="wfn" type="text" [(ngModel)]="form.firstName" autocomplete="given-name"
                     [class.field--error]="fieldError('firstName')" />
            </div>
            <div class="field">
              <label for="wln">Last name *</label>
              <input id="wln" type="text" [(ngModel)]="form.lastName" autocomplete="family-name"
                     [class.field--error]="fieldError('lastName')" />
            </div>
          </div>
          <div class="field-row">
            <div class="field">
              <label for="wdob">Date of birth</label>
              <input id="wdob" type="date" [(ngModel)]="form.dateOfBirth" />
            </div>
            <div class="field">
              <label for="wemail">Email</label>
              <input id="wemail" type="email" [(ngModel)]="form.email" autocomplete="email" />
            </div>
          </div>
        }

        <div class="field" style="margin-top:4px;">
          <label for="wcc">Chief complaint *</label>
          <input id="wcc" type="text" [(ngModel)]="form.chiefComplaint"
                 placeholder="e.g. Chest pain, Follow-up, Medication review"
                 [class.field--error]="fieldError('chiefComplaint')" />
        </div>

        @if (submitError) {
          <p class="form-error" role="alert">{{ submitError }}</p>
        }

        <div style="display:flex;gap:12px;margin-top:20px;">
          <button class="btn btn--secondary" (click)="resetForm()">Cancel</button>
          <button class="btn btn--primary" (click)="addToQueue()" [disabled]="submitting">
            {{ submitting ? 'Adding to queue…' : 'Add to queue' }}
          </button>
        </div>
      </div>
    }
  </div>

  <!-- Right: today's queue preview -->
  <div class="queue-summary">
    <h3>Today's queue ({{ queuePreview.length }})</h3>
    @if (!queuePreview.length) {
      <p style="font-size:13px;color:var(--ctd);">Queue is empty.</p>
    }
    @for (e of queuePreview; track e.id; let i = $index) {
      <div class="queue-entry">
        <span class="queue-pos" [attr.aria-label]="'Position ' + (i + 1)">{{ i + 1 }}</span>
        <div class="queue-entry-info">
          <strong>{{ e.patientName }}</strong>
          <p>{{ e.chiefComplaint }}</p>
        </div>
      </div>
    }
    <a class="btn btn--secondary" routerLink="/staff/queue" style="width:100%;margin-top:16px;justify-content:center;">
      View full queue
    </a>
  </div>
</div>
  `,
  styles: [`
    :root{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .back-link{display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--cp);text-decoration:none;margin-bottom:16px;}
    .back-link:hover{text-decoration:underline;}
    h1{font-size:24px;font-weight:600;margin-bottom:4px;color:var(--ct1);}
    .page-sub{font-size:14px;color:var(--ct2);margin-bottom:24px;}
    .layout{display:grid;grid-template-columns:1fr 360px;gap:24px;max-width:1000px;}
    .btn{display:inline-flex;align-items:center;gap:8px;padding:9px 16px;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;border:1px solid transparent;text-decoration:none;}
    .btn:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .btn:disabled{opacity:.5;cursor:not-allowed;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not(:disabled){background:var(--cph);}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover:not(:disabled){background:var(--cs1);}
    .search-card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:24px;margin-bottom:16px;}
    .search-card h2{font-size:16px;font-weight:600;margin-bottom:14px;}
    .search-row{display:flex;gap:8px;}
    .search-input{flex:1;padding:10px 14px;border:1px solid var(--cb);border-radius:var(--r2);font-size:14px;font-family:var(--ff);}
    .search-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .result-card{background:var(--cps);border:1px solid var(--cp);border-radius:var(--r2);padding:16px;display:flex;align-items:center;justify-content:space-between;margin-top:12px;}
    .result-info p{font-size:13px;color:var(--ct2);margin:4px 0 0;}
    .result-info strong{font-size:15px;color:var(--ct1);}
    .not-found{background:var(--cs1);border:1px dashed var(--cb);border-radius:var(--r2);padding:20px;margin-top:12px;text-align:center;}
    .not-found p{font-size:14px;color:var(--ct2);margin-bottom:12px;}
    .minimal-form{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:24px;}
    .minimal-form h2{font-size:16px;font-weight:600;margin-bottom:16px;}
    .field{margin-bottom:14px;}
    .field label{display:block;font-size:13px;font-weight:500;margin-bottom:6px;color:var(--ct2);}
    .field input{width:100%;padding:8px 12px;border:1px solid var(--cb);border-radius:var(--r2);font-size:14px;font-family:var(--ff);}
    .field input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .field--error{border-color:var(--ce)!important;}
    .field-row{display:grid;grid-template-columns:1fr 1fr;gap:12px;}
    .selected-info{background:var(--cps);border:1px solid var(--cp);border-radius:var(--r2);padding:12px 16px;margin-bottom:16px;}
    .selected-info p{font-size:13px;color:var(--ct2);margin-top:4px;}
    .form-error{font-size:13px;color:var(--ce);margin-top:8px;}
    .queue-summary{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:20px;}
    .queue-summary h3{font-size:14px;font-weight:600;margin-bottom:16px;}
    .queue-entry{display:flex;align-items:center;gap:12px;padding:10px 0;border-bottom:1px solid var(--cs2);}
    .queue-entry:last-child{border-bottom:none;}
    .queue-pos{width:28px;height:28px;border-radius:50%;background:var(--cp);color:#fff;display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:600;flex-shrink:0;}
    .queue-entry-info strong{font-size:13px;display:block;}
    .queue-entry-info p{font-size:12px;color:var(--ct2);margin:2px 0 0;}
    @media(max-width:900px){.layout{grid-template-columns:1fr;}.field-row{grid-template-columns:1fr;}}
  `]
})
export class WalkinComponent implements OnInit {
  searchQuery    = '';
  searching      = false;
  searched       = false;
  foundPatient: PatientResult | null = null;
  selectedPatient: PatientResult | null = null;
  showForm       = false;
  submitting     = false;
  submitError    = '';
  validationErrors: Set<string> = new Set();
  queuePreview: QueueEntry[] = [];
  form = { firstName: '', lastName: '', dateOfBirth: '', email: '', chiefComplaint: '' };

  constructor(private svc: AppointmentService, private router: Router) {}

  ngOnInit(): void { this.loadQueuePreview(); }

  private loadQueuePreview(): void {
    this.svc.getQueue().subscribe({ next: d => this.queuePreview = d.slice(0, 5), error: () => {} });
  }

  searchPatient(): void {
    if (!this.searchQuery.trim()) return;
    this.searching = true;
    this.svc.searchPatients(this.searchQuery).subscribe({
      next: results => {
        this.foundPatient = results.length > 0 ? results[0] : null;
        this.searched = true;
        this.searching = false;
      },
      error: () => { this.foundPatient = null; this.searched = true; this.searching = false; }
    });
  }

  selectPatient(): void {
    this.selectedPatient = this.foundPatient;
    this.showForm = true;
    this.form.chiefComplaint = '';
    this.submitError = '';
  }

  useNewForm(): void {
    this.selectedPatient = null;
    this.showForm = true;
    this.form = { firstName: '', lastName: '', dateOfBirth: '', email: '', chiefComplaint: '' };
    this.submitError = '';
  }

  resetForm(): void {
    this.showForm = false;
    this.selectedPatient = null;
    this.foundPatient = null;
    this.searched = false;
    this.searchQuery = '';
    this.submitError = '';
    this.validationErrors.clear();
  }

  fieldError(field: string): boolean { return this.validationErrors.has(field); }

  addToQueue(): void {
    this.validationErrors.clear();
    this.submitError = '';

    if (!this.form.chiefComplaint.trim()) this.validationErrors.add('chiefComplaint');
    if (!this.selectedPatient) {
      if (!this.form.firstName.trim()) this.validationErrors.add('firstName');
      if (!this.form.lastName.trim())  this.validationErrors.add('lastName');
    }
    if (this.validationErrors.size > 0) {
      this.submitError = 'Please fill in all required fields.';
      return;
    }

    const payload: any = { chiefComplaint: this.form.chiefComplaint };
    if (this.selectedPatient) {
      payload.patientId = this.selectedPatient.id;
    } else {
      payload.firstName   = this.form.firstName;
      payload.lastName    = this.form.lastName;
      payload.dateOfBirth = this.form.dateOfBirth || undefined;
      payload.email       = this.form.email || undefined;
    }

    this.submitting = true;
    this.svc.registerWalkIn(payload).subscribe({
      next: () => this.router.navigate(['/staff/queue']),
      error: err => {
        this.submitting = false;
        this.submitError = err?.error?.error ?? 'Failed to add to queue. Please try again.';
      }
    });
  }

  formatDate(iso: string): string {
    if (!iso) return '';
    return new Date(iso).toLocaleDateString('en-GB');
  }
}




interface WalkInPayload {
  patientId?: number;
  firstName?: string;
  lastName?: string;
  dateOfBirth?: string;
  email?: string;
  chiefComplaint: string;
}

