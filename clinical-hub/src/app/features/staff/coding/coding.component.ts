import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  CodingService,
  CodeSuggestion,
  SuggestionStatus,
} from '../../../core/services/coding.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-staff-coding',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  template: `
<div class="page">
  <a routerLink="/staff/schedule" class="back-link">← Back to patient</a>

  <div class="page-header">
    <div>
      <h1>Code Verification</h1>
      <p class="patient-ctx">Patient #{{ patientId }} &nbsp;·&nbsp; Reviewing AI-suggested codes</p>
    </div>
    <button class="btn btn--primary" (click)="openAcceptAllModal()"
            [disabled]="pendingCount === 0" [class.btn--disabled]="pendingCount === 0">
      Accept all pending ({{ pendingCount }})
    </button>
  </div>

  <!-- ICD-10 section -->
  <section class="code-section" *ngIf="icdCodes.length">
    <h2 class="code-section__title">
      ICD-10 codes <span class="code-label code-label--icd">ICD-10</span>
    </h2>
    <div class="code-table" role="table" aria-label="ICD-10 code suggestions">
      <div class="code-table-header" role="row">
        <span role="columnheader">Code</span>
        <span role="columnheader">Description</span>
        <span role="columnheader">Confidence</span>
        <span role="columnheader">Status</span>
        <span role="columnheader">Actions</span>
      </div>
      <div *ngFor="let s of icdCodes"
           class="code-row"
           [class.accepted]="s.status === 'Accepted'"
           [class.rejected]="s.status === 'Rejected'"
           role="row">
        <span class="code-val" role="cell">{{ s.code }}</span>
        <span role="cell">
          {{ s.description }}
          <span class="ai-label" aria-label="AI suggested">AI</span>
        </span>
        <div role="cell" class="confidence-meter">
          <div class="confidence-bar-wrap">
            <div class="confidence-fill"
                 [class.confidence-fill--high]="s.confidenceScore >= 80"
                 [class.confidence-fill--medium]="s.confidenceScore >= 50 && s.confidenceScore < 80"
                 [class.confidence-fill--low]="s.confidenceScore < 50"
                 [style.width.%]="s.confidenceScore"></div>
          </div>
          <span class="confidence-pct"
                [class.confidence-pct--high]="s.confidenceScore >= 80"
                [class.confidence-pct--medium]="s.confidenceScore >= 50 && s.confidenceScore < 80"
                [class.confidence-pct--low]="s.confidenceScore < 50">
            {{ s.confidenceScore }}%
          </span>
        </div>
        <span role="cell">
          <span class="status-badge"
                [class.status-badge--accepted]="s.status === 'Accepted'"
                [class.status-badge--pending]="s.status === 'Pending'"
                [class.status-badge--low-conf]="s.status === 'Rejected'">
            {{ s.status }}
          </span>
        </span>
        <div role="cell" class="row-actions" *ngIf="s.status === 'Pending'">
          <button class="btn btn--accept" (click)="accept(s)" [attr.aria-label]="'Accept ' + s.code">✓ Accept</button>
          <button class="btn btn--modify" (click)="startModify(s)" [attr.aria-label]="'Modify ' + s.code">✎ Modify</button>
          <button class="btn btn--reject" (click)="reject(s)" [attr.aria-label]="'Reject ' + s.code">✗ Reject</button>
        </div>
        <div role="cell" *ngIf="s.status !== 'Pending'" class="row-actions">
          <button class="btn btn--modify" (click)="undo(s)" [attr.aria-label]="'Undo ' + s.code">↩ Undo</button>
        </div>
      </div>
    </div>
  </section>

  <!-- CPT section -->
  <section class="code-section" *ngIf="cptCodes.length">
    <h2 class="code-section__title">
      CPT codes <span class="code-label code-label--cpt">CPT</span>
    </h2>
    <div class="code-table" role="table" aria-label="CPT code suggestions">
      <div class="code-table-header" role="row">
        <span role="columnheader">Code</span>
        <span role="columnheader">Description</span>
        <span role="columnheader">Confidence</span>
        <span role="columnheader">Status</span>
        <span role="columnheader">Actions</span>
      </div>
      <div *ngFor="let s of cptCodes"
           class="code-row"
           [class.accepted]="s.status === 'Accepted'"
           [class.rejected]="s.status === 'Rejected'"
           role="row">
        <span class="code-val" role="cell">{{ s.code }}</span>
        <span role="cell">
          {{ s.description }}
          <span class="ai-label" aria-label="AI suggested">AI</span>
        </span>
        <div role="cell" class="confidence-meter">
          <div class="confidence-bar-wrap">
            <div class="confidence-fill"
                 [class.confidence-fill--high]="s.confidenceScore >= 80"
                 [class.confidence-fill--medium]="s.confidenceScore >= 50 && s.confidenceScore < 80"
                 [class.confidence-fill--low]="s.confidenceScore < 50"
                 [style.width.%]="s.confidenceScore"></div>
          </div>
          <span class="confidence-pct"
                [class.confidence-pct--high]="s.confidenceScore >= 80"
                [class.confidence-pct--medium]="s.confidenceScore >= 50 && s.confidenceScore < 80"
                [class.confidence-pct--low]="s.confidenceScore < 50">
            {{ s.confidenceScore }}%
          </span>
        </div>
        <span role="cell">
          <span class="status-badge"
                [class.status-badge--accepted]="s.status === 'Accepted'"
                [class.status-badge--pending]="s.status === 'Pending'"
                [class.status-badge--low-conf]="s.status === 'Rejected'">
            {{ s.status }}
          </span>
        </span>
        <div role="cell" class="row-actions" *ngIf="s.status === 'Pending'">
          <button class="btn btn--accept" (click)="accept(s)" [attr.aria-label]="'Accept ' + s.code">✓ Accept</button>
          <button class="btn btn--modify" (click)="startModify(s)" [attr.aria-label]="'Modify ' + s.code">✎ Modify</button>
          <button class="btn btn--reject" (click)="reject(s)" [attr.aria-label]="'Reject ' + s.code">✗ Reject</button>
        </div>
        <div role="cell" *ngIf="s.status !== 'Pending'" class="row-actions">
          <button class="btn btn--modify" (click)="undo(s)" [attr.aria-label]="'Undo ' + s.code">↩ Undo</button>
        </div>
      </div>
    </div>
  </section>

  <p *ngIf="!icdCodes.length && !cptCodes.length && !loading" class="empty-state">
    No code suggestions found for this patient.
  </p>
  <p *ngIf="loading" class="empty-state" aria-live="polite">Loading…</p>

  <!-- Footer -->
  <div class="action-footer" *ngIf="!loading && suggestions.length">
    <p class="pending-count">
      <strong>{{ pendingCount }}</strong> pending · {{ acceptedCount }} accepted · {{ rejectedCount }} rejected
    </p>
    <div style="display:flex;gap:12px;">
      <button class="btn btn--secondary" routerLink="/staff/schedule">Cancel</button>
      <button class="btn btn--primary"
              [disabled]="pendingCount > 0"
              [class.btn--disabled]="pendingCount > 0"
              (click)="completeCoding()">
        Complete coding
      </button>
    </div>
  </div>
</div>

<!-- Modify modal -->
<div class="modal-backdrop" [class.open]="modifyTarget" role="dialog" aria-modal="true"
     aria-label="Modify code" (keydown.escape)="modifyTarget = null">
  <div class="modal" *ngIf="modifyTarget" (click)="$event.stopPropagation()">
    <h2 class="modal__title">Modify code</h2>
    <div class="modal__body">
      <label class="filter-field" style="margin-bottom:12px;">
        <span style="font-size:12px;font-weight:500;color:var(--ct2);">Code</span>
        <input class="field-input" [(ngModel)]="modifyCode" aria-label="Modified code" />
      </label>
      <label class="filter-field">
        <span style="font-size:12px;font-weight:500;color:var(--ct2);">Description</span>
        <input class="field-input" [(ngModel)]="modifyDesc" aria-label="Modified description" />
      </label>
    </div>
    <div class="modal__actions">
      <button class="btn btn--secondary" (click)="modifyTarget = null">Cancel</button>
      <button class="btn btn--primary" (click)="confirmModify()">Save changes</button>
    </div>
  </div>
</div>

<!-- Accept-all confirmation modal -->
<div class="modal-backdrop" [class.open]="showAcceptAllModal" role="dialog" aria-modal="true"
     aria-label="Accept all codes" (keydown.escape)="showAcceptAllModal = false">
  <div class="modal" (click)="$event.stopPropagation()">
    <h2 class="modal__title">Accept all pending codes?</h2>
    <p class="modal__body">This will accept all {{ pendingCount }} pending suggestions. You can still undo individual codes afterwards.</p>
    <div class="modal__actions">
      <button class="btn btn--secondary" (click)="showAcceptAllModal = false">Cancel</button>
      <button class="btn btn--primary" (click)="acceptAll()">Accept all</button>
    </div>
  </div>
</div>
  `,
  styles: [`
    :host { --cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;--cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--cw:#D4820A;--ci:#1C6EA4;--cib:#E8F0F8;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:4px;--r3:8px;--nh:56px;--sw:240px; }
    *,*::before,*::after{box-sizing:border-box;}
    .page{padding:32px;min-height:100vh;font-family:var(--ff);font-size:14px;color:var(--ct1);}
    .back-link{display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--cp);text-decoration:none;margin-bottom:16px;}
    .back-link:hover{text-decoration:underline;}
    .page-header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:8px;}
    h1{font-size:24px;font-weight:600;margin:0;}
    .patient-ctx{font-size:14px;color:var(--ct2);margin-top:4px;margin-bottom:24px;}
    .code-section{margin-bottom:24px;}
    .code-section__title{font-size:16px;font-weight:600;margin-bottom:12px;display:flex;align-items:center;gap:10px;}
    .code-label{font-size:12px;font-weight:500;padding:2px 8px;border-radius:var(--r2);}
    .code-label--icd{background:var(--cib);color:var(--ci);}
    .code-label--cpt{background:var(--cps);color:var(--cp);}
    .code-table{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);overflow:hidden;}
    .code-table-header{display:grid;grid-template-columns:100px 1fr 180px 100px 150px;padding:10px 16px;border-bottom:2px solid var(--cb);font-size:12px;font-weight:600;color:var(--ct2);text-transform:uppercase;letter-spacing:.04em;}
    .code-row{display:grid;grid-template-columns:100px 1fr 180px 100px 150px;padding:14px 16px;border-bottom:1px solid var(--cs2);align-items:center;}
    .code-row:last-child{border-bottom:none;}
    .code-row:hover{background:var(--cs1);}
    .code-row.accepted{background:var(--cokb);}
    .code-row.rejected{background:var(--ceb);opacity:.6;}
    .code-val{font-family:monospace;font-size:13px;font-weight:600;}
    .confidence-meter{display:flex;flex-direction:column;gap:4px;}
    .confidence-bar-wrap{height:8px;background:var(--cs2);border-radius:4px;overflow:hidden;width:100px;}
    .confidence-fill{height:100%;border-radius:4px;transition:width .3s;}
    .confidence-fill--high{background:var(--cok);}
    .confidence-fill--medium{background:var(--cw);}
    .confidence-fill--low{background:var(--ce);}
    .confidence-pct{font-size:13px;font-weight:600;}
    .confidence-pct--high{color:var(--cok);}
    .confidence-pct--medium{color:var(--cw);}
    .confidence-pct--low{color:var(--ce);}
    .row-actions{display:flex;gap:6px;}
    .btn{display:inline-flex;align-items:center;gap:4px;padding:5px 12px;border-radius:var(--r2);font-size:12px;font-weight:500;cursor:pointer;border:1px solid transparent;}
    .btn:focus-visible{outline:none;box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .btn--accept{background:var(--cokb);color:var(--cok);border-color:var(--cok);}
    .btn--accept:hover{background:var(--cok);color:#fff;}
    .btn--modify{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--modify:hover{background:var(--cs1);}
    .btn--reject{color:var(--ce);border-color:var(--ce);background:var(--cs0);}
    .btn--reject:hover{background:var(--ceb);}
    .btn--primary{background:var(--cp);color:#fff;padding:10px 20px;font-size:14px;}
    .btn--primary:hover:not([disabled]){background:var(--cph);}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);padding:10px 20px;font-size:14px;}
    .btn--secondary:hover{background:var(--cs1);}
    .btn--disabled{opacity:.5;cursor:not-allowed;}
    .status-badge{display:inline-flex;align-items:center;font-size:11px;font-weight:500;padding:2px 6px;border-radius:var(--r2);}
    .status-badge--accepted{background:var(--cokb);color:var(--cok);}
    .status-badge--pending{background:var(--cs2);color:var(--ct2);}
    .status-badge--low-conf{background:var(--ceb);color:var(--ce);}
    .action-footer{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r2);padding:16px 20px;display:flex;align-items:center;justify-content:space-between;margin-top:24px;}
    .pending-count{font-size:14px;color:var(--ct2);}
    .pending-count strong{color:var(--ct1);}
    .modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:500;display:none;align-items:center;justify-content:center;}
    .modal-backdrop.open{display:flex;}
    .modal{background:var(--cs0);border-radius:var(--r3);padding:24px;max-width:440px;width:90%;}
    .modal__title{font-size:18px;font-weight:600;margin-bottom:8px;}
    .modal__body{font-size:14px;color:var(--ct2);margin-bottom:24px;}
    .modal__actions{display:flex;gap:12px;justify-content:flex-end;}
    .field-input{padding:7px 10px;border:1px solid var(--cb);border-radius:var(--r2);font-size:13px;font-family:var(--ff);width:100%;}
    .field-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.35);}
    .filter-field{display:flex;flex-direction:column;gap:6px;margin-bottom:12px;}
    .ai-label{display:inline-flex;align-items:center;padding:1px 6px;border-radius:var(--r2);font-size:10px;font-weight:600;background:var(--cib);color:var(--ci);margin-left:6px;}
    .empty-state{text-align:center;padding:48px;color:var(--ct2);}
  `]
})
export class CodingComponent implements OnInit {
  patientId = 0;
  suggestions: CodeSuggestion[] = [];
  loading = true;
  showAcceptAllModal = false;
  modifyTarget: CodeSuggestion | null = null;
  modifyCode = '';
  modifyDesc = '';

  constructor(private route: ActivatedRoute, private codingSvc: CodingService, private authSvc: AuthService) {}

  ngOnInit(): void {
    this.patientId = Number(this.route.snapshot.queryParamMap.get('patientId') ?? 0);
    if (this.patientId) {
      this.loadSuggestions();
    } else {
      this.loading = false;
    }
  }

  get icdCodes() { return this.suggestions.filter(s => s.codeType === 'ICD10'); }
  get cptCodes() { return this.suggestions.filter(s => s.codeType === 'CPT'); }
  get pendingCount() { return this.suggestions.filter(s => s.status === 'Pending').length; }
  get acceptedCount() { return this.suggestions.filter(s => s.status === 'Accepted').length; }
  get rejectedCount() { return this.suggestions.filter(s => s.status === 'Rejected').length; }

  private loadSuggestions(): void {
    this.loading = true;
    this.codingSvc.getSuggestions(this.patientId).subscribe({
      next: data => { this.suggestions = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  accept(s: CodeSuggestion): void {
    const verifiedById = Number(this.authSvc.getCurrentUserId() ?? 0);
    this.codingSvc.patchSuggestion(s.id, { status: 'Accepted', verifiedById }).subscribe(() => {
      s.status = 'Accepted';
    });
  }

  reject(s: CodeSuggestion): void {
    this.codingSvc.patchSuggestion(s.id, { status: 'Rejected' }).subscribe(() => {
      s.status = 'Rejected';
    });
  }

  undo(s: CodeSuggestion): void {
    this.codingSvc.patchSuggestion(s.id, { status: 'Pending' }).subscribe(() => {
      s.status = 'Pending';
    });
  }

  startModify(s: CodeSuggestion): void {
    this.modifyTarget = s;
    this.modifyCode = s.code;
    this.modifyDesc = s.description;
  }

  confirmModify(): void {
    if (!this.modifyTarget) return;
    const target = this.modifyTarget;
    this.codingSvc.patchSuggestion(target.id, {
      status: 'Modified',
      modifiedCode: this.modifyCode,
      modifiedDescription: this.modifyDesc
    }).subscribe(() => {
      target.status = 'Modified' as SuggestionStatus;
      target.code = this.modifyCode;
      target.description = this.modifyDesc;
      this.modifyTarget = null;
    });
  }

  openAcceptAllModal(): void {
    this.showAcceptAllModal = true;
  }

  acceptAll(): void {
    this.showAcceptAllModal = false;
    const verifiedById = Number(this.authSvc.getCurrentUserId() ?? 0);
    this.codingSvc.acceptAll(this.patientId, verifiedById).subscribe(() => {
      this.suggestions.forEach(s => {
        if (s.status === 'Pending') s.status = 'Accepted';
      });
    });
  }

  completeCoding(): void {
    this.codingSvc.completeCoding(this.patientId).subscribe(() => {
      window.history.back();
    });
  }
}


