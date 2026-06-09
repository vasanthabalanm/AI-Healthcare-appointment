import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule, ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AppointmentService } from '../../../core/services/appointment.service';
import { CodingService } from '../../../core/services/coding.service';

interface ClinicalFieldSummary {
  fieldName: string;
  fieldValue: string;
  confidenceScore: number;
  extractedAt: string;
}

interface PatientView360 {
  patientId: number;
  firstName: string;
  lastName: string;
  email: string;
  verificationStatus: string;
  clinicalFields: Record<string, ClinicalFieldSummary[]>;
  unresolvedConflicts: number;
  hint: string | null;
}

@Component({
  selector: 'app-patient360',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
<a routerLink="/staff/schedule" class="back-link">← Back to schedule</a>

@if (loading()) {
  <p class="page-sub" style="padding:32px 0;">Loading patient view…</p>
}

@if (error()) {
  <p class="text-danger" role="alert">{{ error() }}</p>
}

@if (!loading() && patient()) {
  <!-- Patient header -->
  <div class="patient-header">
    <div style="display:flex;align-items:center;gap:16px;">
      <div class="patient-avatar" aria-hidden="true">{{ initials() }}</div>
      <div>
        <h1 class="patient-name">{{ patient()!.firstName }} {{ patient()!.lastName }}</h1>
        <p class="patient-meta">{{ patient()!.email }}</p>
        <p class="patient-meta">Patient ID: {{ patient()!.patientId }}</p>
      </div>
    </div>
    <div style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;">
      <span class="verification-badge" [ngClass]="verificationClass()">
        {{ verificationLabel() }}
      </span>
      @if (verificationLabel() !== 'Verified') {
        <button class="btn btn--verify" (click)="verifyPatient()" [disabled]="verifying()">
          <svg width="14" height="14" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7"/></svg>
          {{ verifying() ? 'Verifying…' : 'Verify patient' }}
        </button>
      }
      <button class="btn btn--secondary" (click)="generateCodes()" [disabled]="generatingCodes() || verificationLabel() !== 'Verified'" [title]="verificationLabel() !== 'Verified' ? 'Verify patient first' : ''">
        {{ generatingCodes() ? 'Generating…' : 'Generate codes' }}
      </button>
    </div>
  </div>

  <!-- Conflict banner -->
  @if (patient()!.unresolvedConflicts > 0) {
    <div class="conflict-banner" role="alert">
      <div class="conflict-banner__left">
        <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
          <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
        </svg>
        {{ patient()!.unresolvedConflicts }} data conflict{{ patient()!.unresolvedConflicts > 1 ? 's' : '' }} require{{ patient()!.unresolvedConflicts === 1 ? 's' : '' }} review
      </div>
    </div>
  }

  <!-- Hint: no documents -->
  @if (patient()!.hint) {
    <div class="hint-banner" role="note">
      <svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zm-1 4a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd"/>
      </svg>
      {{ patient()!.hint }}
    </div>
  }

  <!-- Demographics section (always shown) -->
  <div class="section-card">
    <button class="section-header" [attr.aria-expanded]="sectionOpen('demographics')"
            (click)="toggleSection('demographics')" aria-controls="section-demographics">
      <h2>Demographics</h2>
      <svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"
           [style.transform]="sectionOpen('demographics') ? 'rotate(0)' : 'rotate(-90deg)'"
           style="transition:transform .2s;">
        <path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/>
      </svg>
    </button>
    @if (sectionOpen('demographics')) {
      <div class="section-body" id="section-demographics">
        <div class="data-row"><span class="data-label">Full name</span><span class="data-value">{{ patient()!.firstName }} {{ patient()!.lastName }}</span></div>
        <div class="data-row"><span class="data-label">Email</span><span class="data-value">{{ patient()!.email }}</span></div>
        <div class="data-row"><span class="data-label">Verification</span><span class="data-value">{{ verificationLabel() }}</span></div>
      </div>
    }
  </div>

  <!-- Clinical field sections (from extracted docs) -->
  @for (entry of clinicalSections(); track entry.key) {
    <div class="section-card">
      <button class="section-header" [attr.aria-expanded]="sectionOpen(entry.key)"
              (click)="toggleSection(entry.key)" [attr.aria-controls]="'section-' + entry.key">
        <h2>{{ entry.label }}</h2>
        <svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"
             [style.transform]="sectionOpen(entry.key) ? 'rotate(0)' : 'rotate(-90deg)'"
             style="transition:transform .2s;">
          <path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/>
        </svg>
      </button>
      @if (sectionOpen(entry.key)) {
        <div class="section-body" [id]="'section-' + entry.key">
          @for (field of entry.fields; track field.fieldName) {
            <div class="data-row">
              <span class="data-label">{{ field.fieldName }}</span>
              <div class="data-value-wrap">
                <span class="data-value">{{ field.fieldValue }}</span>
                @if (field.confidenceScore < 0.7) {
                  <span class="conf-badge conf-badge--low" [attr.aria-label]="'Low confidence: ' + (field.confidenceScore * 100 | number:'1.0-0') + '%'">
                    Low confidence
                  </span>
                }
              </div>
            </div>
          }
          @if (entry.fields.length === 0) {
            <p class="section-empty">No {{ entry.label | lowercase }} data available.</p>
          }
        </div>
      }
    </div>
  }

  <!-- Intake summary section -->
  <div class="section-card">
    <button class="section-header" [attr.aria-expanded]="sectionOpen('intake')"
            (click)="toggleSection('intake')" aria-controls="section-intake">
      <h2>Intake summary</h2>
      <svg width="16" height="16" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"
           [style.transform]="sectionOpen('intake') ? 'rotate(0)' : 'rotate(-90deg)'"
           style="transition:transform .2s;">
        <path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/>
      </svg>
    </button>
    @if (sectionOpen('intake')) {
      <div class="section-body" id="section-intake">
        @if (hasIntake()) {
          @for (field of intakeFields(); track field.fieldName) {
            <div class="data-row">
              <span class="data-label">{{ field.fieldName }}</span>
              <span class="data-value">{{ field.fieldValue }}</span>
            </div>
          }
        } @else {
          <p class="section-empty">No intake submitted yet.</p>
        }
      </div>
    }
  </div>
}
  `,
  styles: [`
    :root{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;--ce:#C0392B;--ceb:#FDECEA;--cw:#D4820A;--cwb:#FEF5E7;--cok:#1A7A4A;--cokb:#E8F5EE;--ci:#1C6EA4;--cib:#E8F0F8;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .back-link{display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--cp);text-decoration:none;margin-bottom:16px;}
    .back-link:hover{text-decoration:underline;}
    .btn{display:inline-flex;align-items:center;gap:8px;padding:10px 20px;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;border:1px solid transparent;text-decoration:none;}
    .btn:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover{background:var(--cph);}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover{background:var(--cs1);}
    .btn--secondary:disabled{opacity:.5;cursor:not-allowed;}
    .btn--verify{background:var(--cokb);border-color:var(--cok);color:var(--cok);}
    .btn--verify:hover{background:#d2eddd;}
    .btn--verify:disabled{opacity:.6;cursor:not-allowed;}
    .patient-header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:16px;flex-wrap:wrap;gap:12px;}
    .patient-avatar{width:48px;height:48px;border-radius:50%;background:var(--cp);color:#fff;display:flex;align-items:center;justify-content:center;font-size:18px;font-weight:700;flex-shrink:0;}
    .patient-name{font-size:24px;font-weight:700;color:var(--ct1);}
    .patient-meta{font-size:13px;color:var(--ct2);margin-top:2px;}
    .verification-badge{display:inline-flex;align-items:center;padding:4px 10px;border-radius:var(--r2);font-size:12px;font-weight:600;}
    .badge--verified{background:var(--cokb);color:var(--cok);}
    .badge--unverified{background:var(--cwb);color:var(--cw);}
    .badge--pending{background:var(--cib);color:var(--ci);}
    .conflict-banner{background:var(--cwb);border:1px solid var(--cw);border-radius:var(--r2);padding:12px 16px;display:flex;align-items:center;justify-content:space-between;margin-bottom:16px;}
    .conflict-banner__left{display:flex;align-items:center;gap:8px;font-size:14px;color:var(--cw);font-weight:500;}
    .hint-banner{display:flex;align-items:center;gap:8px;padding:12px 16px;background:var(--cib);border-radius:var(--r2);font-size:13px;color:var(--ci);margin-bottom:16px;}
    .section-card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);margin-bottom:10px;overflow:hidden;}
    .section-header{display:flex;align-items:center;justify-content:space-between;padding:14px 20px;cursor:pointer;background:none;border:none;width:100%;text-align:left;border-bottom:1px solid var(--cs2);}
    .section-header:hover{background:var(--cs1);}
    .section-header:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35) inset;}
    .section-header h2{font-size:15px;font-weight:600;color:var(--ct1);margin:0;}
    .section-body{padding:4px 0;}
    .section-empty{padding:12px 20px;font-size:13px;color:var(--ctd);}
    .data-row{display:flex;justify-content:space-between;align-items:flex-start;padding:10px 20px;border-bottom:1px solid var(--cs2);font-size:13px;}
    .data-row:last-child{border-bottom:none;}
    .data-label{color:var(--ct2);font-weight:500;min-width:160px;}
    .data-value{color:var(--ct1);font-weight:500;}
    .data-value-wrap{display:flex;align-items:center;gap:8px;}
    .conf-badge{display:inline-flex;align-items:center;padding:2px 6px;border-radius:var(--r2);font-size:11px;font-weight:500;}
    .conf-badge--low{background:var(--cwb);color:var(--cw);}
    .page-sub{font-size:14px;color:var(--ct2);}
    .text-danger{color:var(--ce);font-size:14px;}
  `]
})
export class Patient360Component implements OnInit {
  patient  = signal<PatientView360 | null>(null);
  loading  = signal(true);
  error    = signal('');

  private openSections = signal<Set<string>>(new Set(['demographics', 'intake']));

  generatingCodes = signal(false);
  verifying       = signal(false);

  constructor(
    private svc: AppointmentService,
    private route: ActivatedRoute,
    private router: Router,
    private codingSvc: CodingService
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.svc.get360View(id).subscribe({
      next: data => { this.patient.set(data); this.loading.set(false); },
      error: err  => {
        this.error.set(err?.error?.error ?? 'Failed to load patient view.');
        this.loading.set(false);
      }
    });
  }

  verifyPatient(): void {
    const id = this.patient()!.patientId;
    this.verifying.set(true);
    this.svc.verifyPatient(id).subscribe({
      next: () => {
        this.verifying.set(false);
        this.patient.update(p => p ? { ...p, verificationStatus: 'Verified' } : p);
        this.error.set('');
      },
      error: (err) => {
        this.verifying.set(false);
        this.error.set(err?.error?.error ?? 'Failed to verify patient.');
      }
    });
  }

  generateCodes(): void {
    const id = this.patient()!.patientId;
    this.generatingCodes.set(true);
    forkJoin([
      this.codingSvc.generateCodes(id, 'ICD10'),
      this.codingSvc.generateCodes(id, 'CPT')
    ]).subscribe({
      next: () => {
        this.generatingCodes.set(false);
        this.router.navigate(['/staff/coding'], { queryParams: { patientId: id } });
      },
      error: (err) => {
        this.generatingCodes.set(false);
        this.error.set(err?.error?.error ?? 'Failed to generate codes. Ensure the patient is Verified.');
      }
    });
  }

  readonly initials = computed(() => {
    const p = this.patient();
    if (!p) return '?';
    return ((p.firstName[0] ?? '') + (p.lastName[0] ?? '')).toUpperCase();
  });

  verificationClass(): Record<string, boolean> {
    const raw = this.patient()?.verificationStatus ?? '';
    // API may return enum string ("Verified") or legacy integer string ("1" / "0")
    const isVerified = raw === 'Verified' || raw === '1' || (raw as unknown) === 1;
    const v = isVerified ? 'Verified' : (raw === 'Pending' ? 'Pending' : 'Unverified');
    return {
      'badge--verified':   v === 'Verified',
      'badge--unverified': v === 'Unverified',
      'badge--pending':    v === 'Pending',
    };
  }

  verificationLabel(): string {
    const raw = this.patient()?.verificationStatus ?? '';
    if (raw === 'Verified' || raw === '1' || (raw as unknown) === 1) return 'Verified';
    if (raw === 'Pending') return 'Pending';
    return 'Unverified';
  }

  readonly clinicalSections = computed(() => {
    const p = this.patient();
    if (!p) return [];
    const labelMap: Record<string, string> = {
      Medication:     'Medications',
      Allergy:        'Allergies',
      Diagnosis:      'Diagnoses',
      MedicalHistory: 'Medical history',
      ChiefComplaint: 'Chief complaint',
      Vitals:         'Vitals',
    };
    return Object.entries(p.clinicalFields)
      .filter(([key]) => key !== 'Intake')
      .map(([key, fields]) => ({
        key,
        label: labelMap[key] ?? key,
        fields
      }));
  });

  readonly intakeFields = computed(() => {
    const p = this.patient();
    return p?.clinicalFields?.['Intake'] ?? p?.clinicalFields?.['ChiefComplaint'] ?? [];
  });

  readonly hasIntake = computed(() => this.intakeFields().length > 0);

  sectionOpen(key: string): boolean { return this.openSections().has(key); }

  toggleSection(key: string): void {
    this.openSections.update(s => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }
}
