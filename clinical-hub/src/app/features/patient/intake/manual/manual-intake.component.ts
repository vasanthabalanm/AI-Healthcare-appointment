import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormArray, Validators } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../../environments/environment';

@Component({
  selector: 'app-manual-intake',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, RouterModule],
  template: `
<div class="page-header">
  <div>
    <h1>Intake form</h1>
    <p class="page-sub">Complete your pre-appointment health information.</p>
  </div>
  <a routerLink="/patient/intake" class="ai-switch" aria-label="Switch to AI-assisted intake">
    <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
      <path fill-rule="evenodd" d="M18 10c0 3.866-3.582 7-8 7a8.841 8.841 0 01-4.083-.98L2 17l1.338-3.123C2.493 12.767 2 11.434 2 10c0-3.866 3.582-7 8-7s8 3.134 8 7z" clip-rule="evenodd"/>
    </svg>
    Use AI-assisted intake
  </a>
</div>

<div *ngIf="submitted" class="success-banner" role="status">
  <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/></svg>
  Intake submitted! Your clinical team will review it before your appointment.
  <a routerLink="/patient/dashboard" class="btn btn--secondary" style="margin-left:auto;">Back to dashboard</a>
</div>

<form class="form-container" [formGroup]="form" (ngSubmit)="submit()" novalidate *ngIf="!submitted">

  <!-- Chief Complaint -->
  <div class="form-section">
    <h2 class="form-section__title">Chief complaint</h2>
    <div class="field">
      <label for="chiefComplaint">Reason for visit <span class="required" aria-hidden="true">*</span></label>
      <textarea id="chiefComplaint" formControlName="chiefComplaint" rows="3"
                placeholder="Describe your main symptom or concern, how long it has been present, and severity (1–10)…"
                [class.error]="isInvalid('chiefComplaint')" aria-required="true"></textarea>
      <div class="field-error" *ngIf="isInvalid('chiefComplaint')">Chief complaint is required.</div>
    </div>
  </div>

  <!-- Medications -->
  <div class="form-section">
    <h2 class="form-section__title">Current medications</h2>
    <div id="med-list" formArrayName="medications" aria-label="Medication list">
      <div class="med-row" *ngFor="let ctrl of medications.controls; let i = index">
        <input type="text" [formControl]="asControl(ctrl)"
               [attr.aria-label]="'Medication ' + (i + 1)"
               placeholder="Medication name, dose, frequency" />
        <button class="remove-btn" type="button" (click)="removeMed(i)" [attr.aria-label]="'Remove medication ' + (i + 1)">✕</button>
      </div>
    </div>
    <button class="add-btn" type="button" (click)="addMed()">+ Add medication</button>
  </div>

  <!-- Allergies -->
  <div class="form-section">
    <h2 class="form-section__title">Allergies</h2>
    <div class="field">
      <label for="allergies">Known allergies</label>
      <textarea id="allergies" formControlName="allergies" rows="2"
                placeholder="List any known drug, food, or environmental allergies. Write 'none' if not applicable."></textarea>
    </div>
  </div>

  <!-- Medical History -->
  <div class="form-section">
    <h2 class="form-section__title">Medical history</h2>
    <div class="field">
      <label for="medicalHistory">Relevant history</label>
      <textarea id="medicalHistory" formControlName="medicalHistory" rows="3"
                placeholder="Previous conditions, surgeries, hospitalisations…"></textarea>
    </div>
  </div>

  <div class="submit-area">
    <span class="save-note" *ngIf="!error">Form is not auto-saved. Submit when ready.</span>
    <span class="save-note save-note--error" *ngIf="error" role="alert">{{ error }}</span>
    <div style="display:flex;gap:12px;">
      <button class="btn btn--secondary" type="button" routerLink="/patient/dashboard">Cancel</button>
      <button class="btn btn--primary" type="submit" [disabled]="saving">
        <svg *ngIf="saving" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
             stroke-width="2" style="animation:spin 1s linear infinite" aria-hidden="true">
          <circle cx="12" cy="12" r="10" stroke-dasharray="60" stroke-dashoffset="20"/>
        </svg>
        {{ saving ? 'Submitting…' : 'Submit intake' }}
      </button>
    </div>
  </div>
</form>
  `,
  styles: [`
    :host{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;}
    *{box-sizing:border-box;}
    .page-header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:24px;}
    h1{font-size:24px;font-weight:600;margin:0 0 4px;}
    .page-sub{font-size:14px;color:var(--ct2);margin:0;}
    .ai-switch{font-size:13px;color:var(--cp);text-decoration:none;display:flex;align-items:center;gap:4px;white-space:nowrap;}
    .ai-switch:hover{text-decoration:underline;}
    .success-banner{display:flex;align-items:center;gap:10px;background:var(--cokb);color:var(--cok);border:1px solid var(--cok);border-radius:var(--r2);padding:12px 16px;margin-bottom:24px;font-size:14px;font-weight:500;}
    .form-container{max-width:700px;}
    .form-section{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:20px;margin-bottom:16px;}
    .form-section__title{font-size:14px;font-weight:600;margin:0 0 16px;padding-bottom:10px;border-bottom:1px solid var(--cs2);}
    .field{margin-bottom:16px;}
    .field:last-child{margin-bottom:0;}
    label{display:block;font-size:13px;font-weight:500;margin-bottom:6px;}
    .required{color:var(--ce);}
    textarea,input[type="text"]{width:100%;padding:8px 12px;border:1px solid var(--cb);border-radius:var(--r2);font-size:14px;font-family:var(--ff);background:var(--cs0);}
    textarea{resize:vertical;min-height:80px;}
    textarea:focus,input[type="text"]:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    textarea.error,input.error{border-color:var(--ce);}
    .field-error{font-size:12px;color:var(--ce);margin-top:4px;}
    .med-row{display:flex;align-items:center;gap:8px;margin-bottom:8px;}
    .med-row input{flex:1;}
    .remove-btn{padding:8px 10px;border:1px solid var(--cb);border-radius:var(--r2);background:var(--cs0);cursor:pointer;color:var(--ct2);font-size:14px;line-height:1;flex-shrink:0;}
    .remove-btn:hover{background:var(--ceb);color:var(--ce);border-color:var(--ce);}
    .add-btn{padding:6px 14px;border:1px dashed var(--cb);border-radius:var(--r2);background:none;cursor:pointer;font-size:13px;color:var(--cp);margin-top:4px;}
    .add-btn:hover{background:var(--cps);}
    .submit-area{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:16px;display:flex;align-items:center;justify-content:space-between;gap:16px;}
    .save-note{font-size:12px;color:var(--ct2);}
    .save-note--error{color:var(--ce);}
    .btn{display:inline-flex;align-items:center;gap:6px;padding:9px 20px;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;border:1px solid transparent;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not([disabled]){background:var(--cph);}
    .btn--primary[disabled]{opacity:.6;cursor:not-allowed;}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);text-decoration:none;}
    .btn--secondary:hover{background:var(--cs1);}
    @keyframes spin{to{transform:rotate(360deg);}}
  `]
})
export class ManualIntakeComponent {
  form = this.fb.group({
    chiefComplaint: ['', Validators.required],
    allergies:      [''],
    medicalHistory: [''],
    medications:    this.fb.array([this.fb.control('')]),
  });

  saving   = false;
  submitted = false;
  error: string | null = null;

  constructor(
    private fb:     FormBuilder,
    private http:   HttpClient,
    private router: Router,
  ) {}

  get medications(): FormArray { return this.form.get('medications') as FormArray; }

  asControl(c: any) { return c; }

  addMed(): void { this.medications.push(this.fb.control('')); }

  removeMed(i: number): void { this.medications.removeAt(i); }

  isInvalid(field: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.invalid && ctrl?.touched);
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.saving = true;
    this.error  = null;

    const meds = (this.medications.value as string[]).filter(m => m.trim()).join('; ');
    const payload = {
      chiefComplaint: this.form.value.chiefComplaint,
      currentMeds:    meds || 'None',
      allergies:      this.form.value.allergies || 'None',
      medicalHistory: this.form.value.medicalHistory || 'None',
    };

    this.http.post(`${environment.apiBaseUrl}/intake/manual`, payload).subscribe({
      next: () => {
        this.saving    = false;
        this.submitted = true;
      },
      error: err => {
        this.saving = false;
        this.error  = err?.error?.error ?? 'Failed to submit intake. Please try again.';
      },
    });
  }
}
