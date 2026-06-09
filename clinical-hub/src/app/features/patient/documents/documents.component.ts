import { Component, OnInit, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { DocumentService, PatientDocument } from '../../../core/services/document.service';

@Component({
  selector: 'app-my-documents',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
<div class="page">
  <div class="page-header">
    <div>
      <h1>My documents</h1>
      <p class="page-sub">Uploaded medical records, referrals, and forms</p>
    </div>
    <button class="btn btn--primary" (click)="openUpload()">
      <svg width="14" height="14" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5"><path stroke-linecap="round" stroke-linejoin="round" d="M12 5v14M5 12h14"/></svg>
      Upload document
    </button>
  </div>

  <!-- Documents table -->
  <div class="doc-card" *ngIf="!loading && docs.length">
    <div class="doc-table" role="table" aria-label="My documents">
      <div class="doc-table-header" role="row">
        <span role="columnheader">Name</span>
        <span role="columnheader">Type</span>
        <span role="columnheader">Scan</span>
        <span role="columnheader">Confidence</span>
        <span role="columnheader">Uploaded</span>
        <span role="columnheader">Actions</span>
      </div>
      <div *ngFor="let d of docs; let last = last" class="doc-row" [class.doc-row--last]="last" role="row">
        <div role="cell" class="doc-name">
          <div class="doc-icon-wrap" [class]="'doc-icon-wrap--' + fileExt(d.fileName)">
            <svg *ngIf="fileExt(d.fileName) === 'pdf'" width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm-1 1.5L18.5 9H13V3.5zM8.5 17h-1v-5h1c1.1 0 1.75.6 1.75 1.5 0 .45-.18.83-.48 1.1l.73 1.4h-1.1l-.6-1.2H8.5V17zm0-2.2h.4c.4 0 .65-.2.65-.55 0-.33-.25-.55-.65-.55H8.5v1.1zm4.35 2.2h-1.6v-5h1.6c1.3 0 2.1.85 2.1 2.5 0 1.65-.8 2.5-2.1 2.5zm-.5-.8h.4c.75 0 1.1-.5 1.1-1.7 0-1.2-.35-1.7-1.1-1.7h-.4V16.2zm5.15.8h-2.25v-5H18v.8h-1.25v1.25H18v.8h-1.25V17H17.5V17z"/></svg>
            <svg *ngIf="fileExt(d.fileName) === 'docx'" width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zM13 3.5L18.5 9H13V3.5zM7.5 13l1.5 4 1.5-4h1l-2 5.5h-1L7 13h.5zm5.5 0h1l1.5 4 1.5-4h1l-2 5.5h-1L13 13z"/></svg>
            <svg *ngIf="['jpg','jpeg','png'].includes(fileExt(d.fileName))" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="M21 15l-5-5L5 21"/></svg>
            <svg *ngIf="!['pdf','docx','jpg','jpeg','png'].includes(fileExt(d.fileName))" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
          </div>
          <div class="doc-name-text">
            <span class="doc-filename">{{ d.fileName }}</span>
          </div>
        </div>
        <span role="cell">
          <span class="type-badge type-badge--{{ fileExt(d.fileName) }}">{{ fileExt(d.fileName).toUpperCase() }}</span>
        </span>
        <span role="cell">
          <span class="scan-badge"
                [class.scan-badge--complete]="d.ocrStatus === 'Complete'"
                [class.scan-badge--pending]="d.ocrStatus === 'Pending' || d.ocrStatus === 'Processing'"
                [class.scan-badge--failed]="d.ocrStatus === 'Failed'">
            <span class="scan-dot"></span>{{ d.ocrStatus }}
          </span>
        </span>
        <span role="cell" class="doc-conf">
          <ng-container *ngIf="d.ocrConfidence != null">
            <div class="conf-wrap">
              <span [class.conf--high]="d.ocrConfidence >= 80"
                    [class.conf--medium]="d.ocrConfidence >= 50 && d.ocrConfidence < 80"
                    [class.conf--low]="d.ocrConfidence < 50"
                    class="conf-val">{{ d.ocrConfidence }}%</span>
              <div class="conf-bar"><div class="conf-fill"
                [class.conf-fill--high]="d.ocrConfidence >= 80"
                [class.conf-fill--medium]="d.ocrConfidence >= 50 && d.ocrConfidence < 80"
                [class.conf-fill--low]="d.ocrConfidence < 50"
                [style.width.%]="d.ocrConfidence"></div></div>
            </div>
          </ng-container>
          <span *ngIf="d.ocrConfidence == null" class="conf-none">—</span>
        </span>
        <span role="cell" class="doc-date">{{ formatDate(d.uploadedAt) }}</span>
        <div role="cell" class="row-actions">
          <button class="icon-btn icon-btn--view" (click)="previewDoc(d)"
                  [attr.aria-label]="'Preview ' + d.fileName" title="Preview document">
            <svg width="15" height="15" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
          </button>
          <button class="icon-btn icon-btn--delete" (click)="confirmDelete(d)"
                  [attr.aria-label]="'Delete ' + d.fileName" title="Delete document">
            <svg width="14" height="14" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
          </button>
        </div>
      </div>
    </div>
  </div>

  <div class="empty" *ngIf="!loading && !docs.length">
    <div class="empty-icon">
      <svg width="40" height="40" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="12" y1="12" x2="12" y2="18"/><line x1="9" y1="15" x2="15" y2="15"/></svg>
    </div>
    <h3>No documents yet</h3>
    <p>Upload your first medical record, referral, or form.</p>
    <button class="btn btn--primary" style="margin-top:16px" (click)="openUpload()">Upload document</button>
  </div>

  <div class="loading-state" *ngIf="loading" aria-live="polite">
    <div class="spinner"></div>
    <span>Loading documents…</span>
  </div>
</div>

<!-- Upload modal -->
<div class="modal-backdrop" [class.open]="showUpload" role="dialog" aria-modal="true"
     aria-label="Upload document" (keydown.escape)="closeUpload()">
  <div class="modal" (click)="$event.stopPropagation()">
    <div class="modal__header">
      <h2 class="modal__title">Upload document</h2>
      <button class="modal-close" (click)="closeUpload()" aria-label="Close">
        <svg width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
      </button>
    </div>
    <div class="dropzone"
         [class.dropzone--active]="dragOver"
         [class.dropzone--filled]="pendingFile"
         (dragover)="onDragOver($event)"
         (dragleave)="dragOver = false"
         (drop)="onDrop($event)"
         (click)="fileInput.click()"
         role="button" tabindex="0"
         aria-label="Drop file here or click to browse"
         (keydown.enter)="fileInput.click()">
      <ng-container *ngIf="!pendingFile">
        <svg width="32" height="32" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.5" class="dz-icon"><path stroke-linecap="round" stroke-linejoin="round" d="M4 16v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2M16 8l-4-4-4 4M12 4v12"/></svg>
        <p class="dz-main">Drop file here or <span class="link">browse</span></p>
        <p class="dz-sub">PDF, JPG, PNG, DOCX · Max 20 MB</p>
      </ng-container>
      <ng-container *ngIf="pendingFile">
        <svg width="28" height="28" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.5" class="dz-icon dz-icon--ready"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
        <p class="dz-main dz-main--ready">{{ pendingFile.name }}</p>
        <p class="dz-sub">{{ formatBytes(pendingFile.size) }} · Click to change</p>
      </ng-container>
    </div>
    <input type="file" #fileInput style="display:none" (change)="onFileChange($event)"
           accept=".pdf,.jpg,.jpeg,.png,.docx">
    <p *ngIf="uploadError" class="upload-error" role="alert">
      <svg width="13" height="13" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
      {{ uploadError }}
    </p>
    <div class="modal__actions">
      <button class="btn btn--secondary" (click)="closeUpload()">Cancel</button>
      <button class="btn btn--primary" [disabled]="!pendingFile || uploading" (click)="upload()">
        <svg *ngIf="uploading" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" class="spin"><path d="M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0"/></svg>
        {{ uploading ? 'Uploading…' : 'Upload' }}
      </button>
    </div>
  </div>
</div>

<!-- Delete confirmation modal -->
<div class="modal-backdrop" [class.open]="deleteTarget != null" role="dialog" aria-modal="true"
     aria-label="Confirm deletion" (keydown.escape)="deleteTarget = null">
  <div class="modal modal--sm" *ngIf="deleteTarget" (click)="$event.stopPropagation()">
    <div class="modal__header">
      <div class="delete-icon-wrap">
        <svg width="20" height="20" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
      </div>
      <div>
        <h2 class="modal__title">Delete document?</h2>
        <p class="modal__body">"<strong>{{ deleteTarget.fileName }}</strong>" will be permanently deleted.</p>
      </div>
    </div>
    <div class="modal__actions">
      <button class="btn btn--secondary" (click)="$event.stopPropagation(); deleteTarget = null">Keep it</button>
      <button class="btn btn--danger-solid" (click)="$event.stopPropagation(); deleteDoc()">Yes, delete</button>
    </div>
  </div>
</div>

<!-- Preview modal -->
<div class="modal-backdrop preview-backdrop" [class.open]="showPreview" role="dialog" aria-modal="true"
     aria-label="Document preview" (click)="closePreview()" (keydown.escape)="closePreview()">
  <div class="preview-shell" (click)="$event.stopPropagation()">
    <div class="preview-header">
      <div class="preview-header-left">
        <div class="preview-file-icon">
          <svg width="14" height="14" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
        </div>
        <span class="preview-title">{{ previewFileName }}</span>
      </div>
      <button class="icon-btn icon-btn--close" (click)="closePreview()" aria-label="Close preview">
        <svg width="16" height="16" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
      </button>
    </div>
    <div class="preview-body">
      <div *ngIf="previewLoading" class="preview-loader" aria-live="polite">
        <div class="spinner"></div><span>Loading preview…</span>
      </div>
      <div *ngIf="previewError" class="preview-error" role="alert">{{ previewError }}</div>
      <ng-container *ngIf="!previewLoading && !previewError && previewUrl != null">
        <img *ngIf="previewIsImage" [src]="previewUrl" [alt]="previewFileName" class="preview-img"/>
        <iframe *ngIf="!previewIsImage && !previewIsDocx"
                [src]="previewUrl" class="preview-frame" title="Document preview"></iframe>
        <div *ngIf="previewIsDocx" class="preview-docx">
          <svg width="48" height="48" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
          <p>DOCX files cannot be previewed in the browser.</p>
          <a [href]="previewUrl" [download]="previewFileName" class="btn btn--primary">Download file</a>
        </div>
      </ng-container>
    </div>
  </div>
</div>
  `,
  styles: [`
    :host{--cp:#0F6B6B;--cph:#0A5050;--cps:#E6F2F2;--cs0:#FFFFFF;--cs1:#F8F9FA;--cs2:#EBEBEB;--cb:#E2E8E8;--ct1:#1A1A1A;--ct2:#5A6A6A;--ctd:#9AADAD;--ce:#C0392B;--ceb:#FDECEA;--cok:#1A7A4A;--cokb:#E8F5EE;--cw:#D4820A;--cwb:#FFF3E0;--cp-pdf:#E53935;--cp-docx:#1565C0;--cp-img:#7B1FA2;--ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;--r2:6px;--r3:12px;--shadow:0 1px 3px rgba(0,0,0,.08),0 1px 2px rgba(0,0,0,.04);}
    *{box-sizing:border-box;margin:0;padding:0;}
    .page{padding:32px;font-family:var(--ff);font-size:14px;color:var(--ct1);max-width:1100px;}
    .page-header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:28px;}
    h1{font-size:22px;font-weight:700;color:var(--ct1);letter-spacing:-.3px;}
    .page-sub{font-size:13px;color:var(--ct2);margin-top:4px;}

    /* Buttons */
    .btn{display:inline-flex;align-items:center;gap:7px;padding:8px 16px;border-radius:var(--r2);font-size:13px;font-weight:500;cursor:pointer;border:1px solid transparent;transition:background .15s,box-shadow .15s;font-family:var(--ff);}
    .btn--primary{background:var(--cp);color:#fff;box-shadow:0 1px 2px rgba(15,107,107,.3);}
    .btn--primary:hover:not([disabled]){background:var(--cph);}
    .btn--primary:disabled{opacity:.55;cursor:not-allowed;}
    .btn--secondary{background:var(--cs0);border-color:var(--cb);color:var(--ct1);}
    .btn--secondary:hover{background:var(--cs1);}
    .btn--danger-solid{background:#C0392B;color:#fff;border-color:#C0392B;}
    .btn--danger-solid:hover{background:#a93226;}

    /* Icon buttons */
    .icon-btn{display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:var(--r2);border:1px solid var(--cb);background:var(--cs0);cursor:pointer;transition:background .15s,border-color .15s,color .15s;color:var(--ct2);}
    .icon-btn--view:hover{background:var(--cps);border-color:var(--cp);color:var(--cp);}
    .icon-btn--delete:hover{background:var(--ceb);border-color:var(--ce);color:var(--ce);}
    .icon-btn--close:hover{background:var(--cs1);}
    .icon-btn--close{border:none;color:var(--ct2);}

    /* Table card */
    .doc-card{background:var(--cs0);border:1px solid var(--cb);border-radius:var(--r3);box-shadow:var(--shadow);overflow:hidden;}
    .doc-table{width:100%;}
    .doc-table-header{display:grid;grid-template-columns:minmax(200px,1fr) 80px 110px 130px 120px 80px;padding:10px 20px;border-bottom:1px solid var(--cb);background:var(--cs1);font-size:11px;font-weight:600;color:var(--ct2);text-transform:uppercase;letter-spacing:.06em;}
    .doc-row{display:grid;grid-template-columns:minmax(200px,1fr) 80px 110px 130px 120px 80px;padding:14px 20px;align-items:center;transition:background .12s;}
    .doc-row:not(.doc-row--last){border-bottom:1px solid var(--cs2);}
    .doc-row:hover{background:var(--cs1);}

    /* File name cell */
    .doc-name{display:flex;align-items:center;gap:12px;min-width:0;}
    .doc-icon-wrap{width:34px;height:34px;border-radius:8px;display:flex;align-items:center;justify-content:center;flex-shrink:0;}
    .doc-icon-wrap--pdf{background:#FDECEA;color:#C0392B;}
    .doc-icon-wrap--docx{background:#E3F2FD;color:#1565C0;}
    .doc-icon-wrap--jpg,.doc-icon-wrap--jpeg,.doc-icon-wrap--png{background:#F3E5F5;color:#7B1FA2;}
    .doc-icon-wrap--default{background:var(--cs2);color:var(--ct2);}
    .doc-filename{font-size:13px;font-weight:500;color:var(--ct1);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:280px;}

    /* Type badge */
    .type-badge{display:inline-flex;padding:2px 7px;border-radius:4px;font-size:10px;font-weight:700;letter-spacing:.05em;}
    .type-badge--pdf{background:#FDECEA;color:#C0392B;}
    .type-badge--docx{background:#E3F2FD;color:#1565C0;}
    .type-badge--jpg,.type-badge--jpeg,.type-badge--png{background:#F3E5F5;color:#7B1FA2;}
    .type-badge--default{background:var(--cs2);color:var(--ct2);}

    /* Scan badge */
    .scan-badge{display:inline-flex;align-items:center;gap:5px;padding:3px 9px;border-radius:20px;font-size:11px;font-weight:600;}
    .scan-dot{width:6px;height:6px;border-radius:50%;flex-shrink:0;}
    .scan-badge--complete{background:var(--cokb);color:var(--cok);}
    .scan-badge--complete .scan-dot{background:var(--cok);}
    .scan-badge--pending{background:var(--cwb);color:var(--cw);}
    .scan-badge--pending .scan-dot{background:var(--cw);}
    .scan-badge--failed{background:var(--ceb);color:var(--ce);}
    .scan-badge--failed .scan-dot{background:var(--ce);}

    /* Confidence */
    .conf-wrap{display:flex;flex-direction:column;gap:4px;}
    .conf-val{font-size:12px;font-weight:600;}
    .conf--high{color:var(--cok);}
    .conf--medium{color:var(--cw);}
    .conf--low{color:var(--ce);}
    .conf-bar{height:3px;width:64px;background:var(--cs2);border-radius:2px;overflow:hidden;}
    .conf-fill{height:100%;border-radius:2px;transition:width .3s;}
    .conf-fill--high{background:var(--cok);}
    .conf-fill--medium{background:var(--cw);}
    .conf-fill--low{background:var(--ce);}
    .conf-none{color:var(--ctd);font-size:13px;}

    .doc-date{font-size:12px;color:var(--ct2);}
    .row-actions{display:flex;gap:6px;}

    /* Empty & Loading states */
    .empty{padding:72px 32px;text-align:center;color:var(--ct2);}
    .empty-icon{display:inline-flex;padding:16px;background:var(--cps);border-radius:50%;color:var(--cp);margin-bottom:16px;}
    .empty h3{font-size:15px;font-weight:600;color:var(--ct1);margin-bottom:6px;}
    .empty p{font-size:13px;}
    .loading-state{display:flex;align-items:center;justify-content:center;gap:10px;padding:60px;color:var(--ct2);font-size:13px;}
    .spinner{width:18px;height:18px;border:2px solid var(--cs2);border-top-color:var(--cp);border-radius:50%;animation:spin .7s linear infinite;}
    @keyframes spin{to{transform:rotate(360deg);}}
    .spin{animation:spin .7s linear infinite;}

    /* Modals */
    .modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:500;display:none;align-items:center;justify-content:center;backdrop-filter:blur(2px);}
    .modal-backdrop.open{display:flex;}
    .modal{background:var(--cs0);border-radius:var(--r3);padding:24px;max-width:480px;width:90%;box-shadow:0 8px 32px rgba(0,0,0,.16);}
    .modal--sm{max-width:400px;}
    .modal__header{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:20px;gap:12px;}
    .modal__title{font-size:16px;font-weight:600;color:var(--ct1);}
    .modal__body{font-size:13px;color:var(--ct2);margin-top:4px;line-height:1.5;}
    .modal__actions{display:flex;gap:10px;justify-content:flex-end;margin-top:20px;}
    .modal-close{background:none;border:none;cursor:pointer;color:var(--ct2);padding:2px;border-radius:4px;display:flex;}
    .modal-close:hover{background:var(--cs1);color:var(--ct1);}
    .delete-icon-wrap{width:40px;height:40px;background:var(--ceb);border-radius:10px;display:flex;align-items:center;justify-content:center;color:var(--ce);flex-shrink:0;}

    /* Dropzone */
    .dropzone{border:2px dashed var(--cb);border-radius:var(--r3);padding:36px 24px;text-align:center;cursor:pointer;transition:border-color .2s,background .2s;margin-bottom:16px;display:flex;flex-direction:column;align-items:center;gap:8px;}
    .dropzone:hover,.dropzone--active{border-color:var(--cp);background:var(--cps);}
    .dropzone--filled{border-color:var(--cp);border-style:solid;background:var(--cps);}
    .dz-icon{color:var(--ctd);}
    .dz-icon--ready{color:var(--cp);}
    .dz-main{font-size:14px;color:var(--ct2);}
    .dz-main--ready{color:var(--ct1);font-weight:500;}
    .dz-sub{font-size:12px;color:var(--ctd);}
    .link{color:var(--cp);text-decoration:underline;}
    .upload-error{font-size:12px;color:var(--ce);margin-bottom:12px;display:flex;align-items:center;gap:5px;}

    /* Preview */
    .preview-backdrop{align-items:center;justify-content:center;padding:20px;}
    .preview-shell{display:flex;flex-direction:column;background:var(--cs0);width:min(1040px,calc(100vw - 40px));height:calc(100vh - 40px);border-radius:var(--r3);overflow:hidden;box-shadow:0 24px 64px rgba(0,0,0,.35);}
    .preview-header{display:flex;align-items:center;justify-content:space-between;padding:10px 14px;border-bottom:1px solid var(--cb);flex-shrink:0;gap:12px;background:var(--cs0);}
    .preview-header-left{display:flex;align-items:center;gap:10px;min-width:0;}
    .preview-file-icon{width:26px;height:26px;background:var(--cps);border-radius:6px;display:flex;align-items:center;justify-content:center;color:var(--cp);flex-shrink:0;}
    .preview-title{font-size:13px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--ct1);}
    .preview-body{flex:1;overflow:hidden;display:flex;align-items:center;justify-content:center;background:#525659;min-height:0;}
    .preview-loader{display:flex;align-items:center;gap:10px;font-size:13px;color:#ddd;}
    .preview-error{font-size:13px;color:var(--ce);padding:32px;background:var(--cs0);border-radius:var(--r2);}
    .preview-img{max-width:100%;max-height:100%;object-fit:contain;background:#fff;border-radius:4px;}
    .preview-frame{width:100%;height:100%;border:none;display:block;}
    .preview-docx{text-align:center;padding:56px 32px;display:flex;flex-direction:column;align-items:center;gap:16px;color:#ccc;}
    .preview-docx p{font-size:14px;}
  `]
})
export class DocumentsComponent implements OnInit {
  @ViewChild('fileInput') fileInput!: ElementRef<HTMLInputElement>;

  docs: PatientDocument[] = [];
  loading = true;
  showUpload = false;
  pendingFile: File | null = null;
  uploading = false;
  uploadError = '';
  dragOver = false;
  deleteTarget: PatientDocument | null = null;

  // Preview state
  showPreview      = false;
  previewUrl: SafeResourceUrl | null = null;
  previewFileName  = '';
  previewIsImage   = false;
  previewIsDocx    = false;
  previewLoading   = false;
  previewError     = '';
  private previewObjectUrl = '';

  constructor(private docSvc: DocumentService, private sanitizer: DomSanitizer) {}

  ngOnInit(): void { this.loadDocs(); }

  private loadDocs(): void {
    this.loading = true;
    this.docSvc.getDocuments().subscribe({
      next: d => { this.docs = d; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  openUpload(): void {
    this.pendingFile = null;
    this.uploadError = '';
    this.showUpload = true;
  }

  closeUpload(): void {
    this.showUpload = false;
    this.pendingFile = null;
  }

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) this.setFile(input.files[0]);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver = true;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver = false;
    const file = event.dataTransfer?.files[0];
    if (file) this.setFile(file);
  }

  private setFile(file: File): void {
    if (file.size > 20 * 1024 * 1024) {
      this.uploadError = 'File exceeds 20 MB limit.';
      return;
    }
    this.uploadError = '';
    this.pendingFile = file;
  }

  upload(): void {
    if (!this.pendingFile) return;
    this.uploading = true;
    this.docSvc.uploadDocument(this.pendingFile).subscribe({
      next: () => {
        this.uploading = false;
        this.closeUpload();
        this.loadDocs();
      },
      error: () => {
        this.uploading = false;
        this.uploadError = 'Upload failed. Please try again.';
      }
    });
  }

  confirmDelete(doc: PatientDocument): void {
    this.deleteTarget = doc;
  }

  deleteDoc(): void {
    if (!this.deleteTarget) return;
    const id = this.deleteTarget.id;
    this.deleteTarget = null;
    this.docSvc.deleteDocument(id).subscribe(() => this.loadDocs());
  }

  previewDoc(doc: PatientDocument): void {
    this.showPreview    = true;
    this.previewLoading = true;
    this.previewError   = '';
    this.previewUrl     = '';
    this.previewFileName = doc.fileName;
    const ext = doc.fileName.split('.').pop()?.toLowerCase() ?? '';
    this.previewIsImage = ['jpg', 'jpeg', 'png'].includes(ext);
    this.previewIsDocx  = ext === 'docx';

    this.docSvc.previewDocument(doc.id).subscribe({
      next: (blob) => {
        if (this.previewObjectUrl) URL.revokeObjectURL(this.previewObjectUrl);
        this.previewObjectUrl = URL.createObjectURL(blob);
        this.previewUrl     = this.sanitizer.bypassSecurityTrustResourceUrl(this.previewObjectUrl);
        this.previewLoading = false;
      },
      error: () => {
        this.previewLoading = false;
        this.previewError   = 'Could not load preview. Please try again.';
      }
    });
  }

  closePreview(): void {
    this.showPreview = false;
    if (this.previewObjectUrl) {
      URL.revokeObjectURL(this.previewObjectUrl);
      this.previewObjectUrl = '';
    }
    this.previewUrl = null;
  }

  fileExt(name: string): string {
    return name.split('.').pop()?.toLowerCase() ?? 'default';
  }

  fileIcon(name: string): string {
    const ext = name.split('.').pop()?.toLowerCase();
    if (ext === 'pdf') return '📄';
    if (['jpg', 'jpeg', 'png'].includes(ext ?? '')) return '🖼️';
    if (ext === 'docx') return '📝';
    return '📎';
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}

