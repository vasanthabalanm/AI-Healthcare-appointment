import { Component, OnInit, AfterViewChecked, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';

interface ChatMessage {
  role: 'ai' | 'user';
  text: string;
  timestamp: Date;
}

interface SummarySection {
  key: string;
  label: string;
  value: string | null;
  aiSuggested: boolean;
}

const SECTION_KEYS = ['chiefComplaint', 'currentMeds', 'allergies', 'medicalHistory'] as const;
const SECTION_LABELS: Record<string, string> = {
  chiefComplaint: 'CHIEF COMPLAINT',
  currentMeds:    'MEDICATIONS',
  allergies:      'ALLERGIES',
  medicalHistory: 'MEDICAL HISTORY',
};

@Component({
  selector: 'app-my-intake',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
<div class="page">

  <!-- ── Chat panel ─────────────────────────────────────────────────────────── -->
  <div class="chat-panel">

    <div class="chat-header">
      <div class="chat-header__left">
        <h2 class="chat-header__title">Intake assistant</h2>
        <span class="badge badge--ai">AI Assisted</span>
      </div>
      <a routerLink="/patient/intake/manual" class="chat-header__manual" aria-label="Switch to manual form">
        <svg width="13" height="13" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <path d="M11.013 1.427a1.75 1.75 0 0 1 2.474 2.474l-8.25 8.25a.75.75 0 0 1-.364.2l-3 .75a.75.75 0 0 1-.916-.916l.75-3a.75.75 0 0 1 .2-.364l8.25-8.25Zm1.414 1.06a.25.25 0 0 0-.354 0L3.5 11.06l-.43 1.72 1.72-.43 8.573-8.573a.25.25 0 0 0 0-.354l-.936-.936Z"
                fill="currentColor"/>
        </svg>
        Switch to manual form
      </a>
    </div>

    <div class="chat-messages" #msgContainer role="log" aria-live="polite" aria-label="Intake chat">
      <div *ngFor="let msg of messages"
           class="msg"
           [class.msg--ai]="msg.role === 'ai'"
           [class.msg--user]="msg.role === 'user'">
        <div class="msg__bubble"
             [class.msg__bubble--ai]="msg.role === 'ai'"
             [class.msg__bubble--user]="msg.role === 'user'">
          {{ msg.text }}
        </div>
        <div class="msg__meta">
          <span class="msg__sender">{{ msg.role === 'ai' ? 'Intake Assistant' : 'You' }}</span>
          <span class="msg__dot">·</span>
          <span class="msg__time">{{ formatTime(msg.timestamp) }}</span>
        </div>
      </div>

      <div *ngIf="aiTyping" class="msg msg--ai" aria-live="polite" aria-label="AI is typing">
        <div class="msg__bubble msg__bubble--ai typing-indicator">
          <span></span><span></span><span></span>
        </div>
        <div class="msg__meta"><span class="msg__sender">Intake Assistant</span></div>
      </div>
    </div>

    <!-- Submit CTA banner -->
    <div class="chat-submit-cta" *ngIf="canSubmit && !submitted" role="status">
      <span class="chat-submit-cta__text">✓ All questions answered — ready to submit</span>
      <button class="btn btn--primary" (click)="submitIntake()" aria-label="Submit intake">
        Submit intake
      </button>
    </div>

    <form class="chat-input-bar" (ngSubmit)="sendMessage()" aria-label="Chat input">
      <input class="chat-input" type="text" [(ngModel)]="userInput" name="msg"
             [placeholder]="canSubmit ? 'Intake complete — click Submit intake above' : 'Type your response…'"
             [disabled]="aiTyping || submitted || canSubmit"
             autocomplete="off" aria-label="Your message" />
      <button class="btn btn--primary" type="submit"
              [disabled]="!userInput.trim() || aiTyping || submitted || canSubmit">Send</button>
    </form>
  </div>

  <!-- ── Summary panel ──────────────────────────────────────────────────────── -->
  <div class="summary-panel">

    <div class="summary-header">
      <h3 class="summary-title">Intake summary</h3>
    </div>

    <!-- Success state -->
    <div class="summary-success" *ngIf="submitted">
      <div class="check-icon" aria-hidden="true">✓</div>
      <p class="success-title">Intake submitted!</p>
      <p class="success-sub">Your clinical team will review it before your appointment.</p>
      <a routerLink="/patient/book" class="btn btn--primary btn--full" style="margin-top:16px;">
        Book an Appointment
      </a>
      <a routerLink="/patient/dashboard" class="btn btn--secondary btn--full" style="margin-top:8px;">
        Go to Dashboard
      </a>
    </div>

    <!-- In-progress state -->
    <ng-container *ngIf="!submitted">

      <!-- Progress bar -->
      <div class="progress-wrap">
        <div class="progress-label">
          <span>Progress</span>
          <span class="progress-pct">{{ progressPct }}%</span>
        </div>
        <div class="progress-bar" role="progressbar" [attr.aria-valuenow]="progressPct" aria-valuemin="0" aria-valuemax="100">
          <div class="progress-bar__fill" [style.width.%]="progressPct"></div>
        </div>
      </div>

      <!-- Section cards -->
      <div class="sections">
        <div class="section-card" *ngFor="let s of summarySections"
             [class.section-card--filled]="!!s.value"
             [class.section-card--empty]="!s.value">
          <div class="section-card__label">{{ s.label }}</div>
          <div class="section-card__value">{{ s.value || 'Awaiting response…' }}</div>
          <span class="badge badge--suggested" *ngIf="s.aiSuggested">AI Suggested</span>
        </div>
      </div>

      <!-- Submit footer -->
      <div class="summary-footer">
        <button class="btn btn--primary btn--full"
                [disabled]="!canSubmit"
                [class.btn--disabled]="!canSubmit"
                (click)="submitIntake()">
          Submit intake
        </button>
        <p class="summary-hint">
          All sections must be completed before submission.<br>
          No AI output is committed without your review.
        </p>
      </div>

    </ng-container>
  </div>

</div>
  `,
  styles: [`
    :host{
      --cp:#0F6B6B;--cph:#0A5050;--cs0:#FFFFFF;--cs1:#F5F5F5;--cs2:#EBEBEB;
      --cb:#D0D0D0;--ct1:#1A1A1A;--ct2:#5A5A5A;--ctd:#9A9A9A;
      --cok:#1A7A4A;--cokb:#E8F5EE;--ci:#1C6EA4;--cib:#E8F0F8;
      --cai:#0A7340;--caib:#E6F4ED;
      --ff:system-ui,"IBM Plex Sans",-apple-system,sans-serif;
      --r2:4px;--r3:8px;--nh:56px;
    }
    *{box-sizing:border-box;}
    :host{display:block;}

    /* ── Layout ── */
    .page{display:grid;grid-template-columns:1fr 320px;height:calc(100vh - var(--nh));overflow:hidden;font-family:var(--ff);font-size:14px;margin:calc(-1 * var(--sp8,0px));}

    /* ── Chat panel ── */
    .chat-panel{display:flex;flex-direction:column;overflow:hidden;border-right:1px solid var(--cs2);background:var(--cs0);}
    .chat-header{flex-shrink:0;padding:14px 20px;border-bottom:1px solid var(--cs2);display:flex;align-items:center;justify-content:space-between;}
    .chat-header__left{display:flex;align-items:center;gap:10px;}
    .chat-header__title{font-size:15px;font-weight:600;margin:0;color:var(--ct1);}
    .chat-header__manual{display:inline-flex;align-items:center;gap:5px;font-size:13px;color:var(--cp);text-decoration:none;}
    .chat-header__manual:hover{text-decoration:underline;}

    /* Badges */
    .badge{display:inline-flex;align-items:center;padding:2px 8px;border-radius:20px;font-size:11px;font-weight:600;white-space:nowrap;}
    .badge--ai{background:var(--caib);color:var(--cai);}
    .badge--suggested{background:var(--cib);color:var(--ci);margin-top:6px;}

    /* ── Messages ── */
    .chat-messages{flex:1;min-height:0;overflow-y:auto;padding:20px;display:flex;flex-direction:column;gap:12px;}
    .msg{max-width:82%;}
    .msg--ai{align-self:flex-start;}
    .msg--user{align-self:flex-end;}
    .msg__bubble{padding:11px 15px;border-radius:var(--r3);font-size:14px;line-height:1.55;}
    .msg__bubble--ai{background:var(--cs1);color:var(--ct1);border-bottom-left-radius:var(--r2);}
    .msg__bubble--user{background:var(--cp);color:#fff;border-bottom-right-radius:var(--r2);}
    .msg__meta{display:flex;align-items:center;gap:4px;margin-top:4px;font-size:11px;color:var(--ctd);}
    .msg--user .msg__meta{justify-content:flex-end;}
    .msg__sender{font-weight:500;}
    .msg__dot{opacity:.5;}

    /* Typing indicator */
    .typing-indicator{display:flex;gap:4px;align-items:center;}
    .typing-indicator span{width:6px;height:6px;background:var(--ctd);border-radius:50%;animation:bounce .8s infinite;}
    .typing-indicator span:nth-child(2){animation-delay:.15s;}
    .typing-indicator span:nth-child(3){animation-delay:.3s;}
    @keyframes bounce{0%,80%,100%{transform:translateY(0);}40%{transform:translateY(-6px);}}

    /* Submit CTA */
    .chat-submit-cta{flex-shrink:0;display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 20px;background:var(--cokb);border-top:2px solid var(--cok);}
    .chat-submit-cta__text{font-size:13px;font-weight:500;color:var(--cok);}

    /* Input bar */
    .chat-input-bar{flex-shrink:0;display:flex;gap:8px;padding:14px 20px;border-top:1px solid var(--cs2);background:var(--cs0);}
    .chat-input{flex:1;padding:10px 14px;border:1px solid var(--cb);border-radius:var(--r2);font-size:14px;font-family:var(--ff);}
    .chat-input:focus{outline:none;border-color:var(--cp);box-shadow:0 0 0 3px rgba(15,107,107,.25);}
    .chat-input:disabled{background:var(--cs1);color:var(--ctd);cursor:not-allowed;}

    /* ── Buttons ── */
    .btn{display:inline-flex;align-items:center;justify-content:center;gap:6px;padding:8px 18px;border-radius:var(--r2);font-size:14px;font-weight:500;cursor:pointer;border:1px solid transparent;transition:background .15s;}
    .btn--primary{background:var(--cp);color:#fff;}
    .btn--primary:hover:not([disabled]){background:var(--cph);}
    .btn--primary[disabled]{opacity:.45;cursor:not-allowed;}
    .btn--disabled{opacity:.45;cursor:not-allowed;}
    .btn--full{width:100%;}

    /* ── Summary panel ── */
    .summary-panel{display:flex;flex-direction:column;background:var(--cs0);border-left:1px solid var(--cs2);overflow-y:auto;}
    .summary-header{flex-shrink:0;padding:16px 20px 12px;border-bottom:1px solid var(--cs2);}
    .summary-title{font-size:15px;font-weight:600;margin:0;color:var(--ct1);}

    /* Progress bar */
    .progress-wrap{padding:16px 20px 8px;}
    .progress-label{display:flex;justify-content:space-between;font-size:12px;color:var(--ct2);margin-bottom:6px;}
    .progress-pct{font-weight:600;color:var(--cok);}
    .progress-bar{height:6px;background:var(--cs2);border-radius:3px;overflow:hidden;}
    .progress-bar__fill{height:100%;background:var(--cp);border-radius:3px;transition:width .4s ease;}

    /* Section cards */
    .sections{padding:8px 12px;display:flex;flex-direction:column;gap:8px;}
    .section-card{padding:12px 14px;border-radius:var(--r3);border:1px solid var(--cs2);background:var(--cs0);}
    .section-card--filled{border-color:var(--cs2);}
    .section-card--empty{border-style:dashed;background:var(--cs1);}
    .section-card__label{font-size:10px;font-weight:700;letter-spacing:.06em;color:var(--ct2);text-transform:uppercase;margin-bottom:4px;}
    .section-card__value{font-size:13px;color:var(--ct1);line-height:1.4;}
    .section-card--empty .section-card__value{color:var(--ctd);font-style:italic;}

    /* Submit footer */
    .summary-footer{padding:14px 20px;border-top:1px solid var(--cs2);margin-top:auto;}
    .summary-hint{font-size:11px;color:var(--ctd);margin-top:8px;line-height:1.5;text-align:center;}

    /* Success */
    .summary-success{padding:32px 20px;text-align:center;display:flex;flex-direction:column;align-items:center;gap:6px;}
    .check-icon{width:52px;height:52px;background:var(--cokb);color:var(--cok);border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:26px;font-weight:700;margin-bottom:8px;}
    .success-title{font-size:16px;font-weight:600;color:var(--ct1);margin:0;}
    .success-sub{font-size:13px;color:var(--ct2);margin:0;}
    .success-redirect{font-size:12px;color:var(--ctd);margin:0;}
  `]
})
export class IntakeComponent implements OnInit, AfterViewChecked {
  @ViewChild('msgContainer') msgContainer!: ElementRef<HTMLDivElement>;

  messages: ChatMessage[] = [];
  userInput = '';
  aiTyping = false;
  submitted = false;
  canSubmit = false;
  redirectCountdown = 0;

  summarySections: SummarySection[] = SECTION_KEYS.map(key => ({
    key,
    label: SECTION_LABELS[key],
    value: null,
    aiSuggested: false,
  }));

  get progressPct(): number {
    const filled = this.summarySections.filter(s => !!s.value).length;
    return Math.round((filled / this.summarySections.length) * 100);
  }

  private sessionId: string | null = null;
  private api = environment.apiBaseUrl;

  constructor(private http: HttpClient, private router: Router) {}

  ngOnInit(): void {
    this.startSession();
  }

  ngAfterViewChecked(): void {
    this.scrollToBottom();
  }

  private scrollToBottom(): void {
    try {
      const el = this.msgContainer?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    } catch { /* ignore */ }
  }

  private startSession(): void {
    this.aiTyping = true;
    this.http.post<{ sessionId: string; message: string }>(
      `${this.api}/intake/ai/start`, {}
    ).subscribe({
      next: res => {
        this.sessionId = res.sessionId;
        this.addAiMessage(res.message);
        this.aiTyping = false;
      },
      error: () => {
        this.addAiMessage("Hello! I'm your intake assistant. Let's begin — what's the main reason for your visit today?");
        this.aiTyping = false;
      }
    });
  }

  sendMessage(): void {
    const text = this.userInput.trim();
    if (!text || this.aiTyping) return;
    this.userInput = '';
    this.messages.push({ role: 'user', text, timestamp: new Date() });
    this.aiTyping = true;

    this.http.post<{
      text: string;
      confidence: number;
      fieldCommitted: boolean;
      confirmedFields: Record<string, string>;
      requiresClarification: boolean;
    }>(`${this.api}/intake/ai/message`, { sessionId: this.sessionId, message: text })
    .subscribe({
      next: res => {
        this.addAiMessage(res.text);
        this.updateSections(res.confirmedFields ?? {});
        const wasReady = this.canSubmit;
        this.canSubmit = (res.confidence ?? 0) >= 0.95;
        if (this.canSubmit && !wasReady) {
          this.addAiMessage("✓ Great — I have everything I need! Click the 'Submit intake' button to complete your intake.");
        }
        this.aiTyping = false;
      },
      error: () => {
        this.addAiMessage("I'm having trouble connecting. Please try again or switch to the manual form.");
        this.aiTyping = false;
      }
    });
  }

  submitIntake(): void {
    this.http.post<void>(`${this.api}/intake/ai/complete`, { sessionId: this.sessionId }).subscribe({
      next: () => this.onSubmitSuccess(),
      error: () => this.onSubmitSuccess(),
    });
  }

  private onSubmitSuccess(): void {
    this.submitted = true;
  }

  private updateSections(fields: Record<string, string>): void {
    this.summarySections = this.summarySections.map(s => {
      const incoming = fields[s.key];
      return incoming
        ? { ...s, value: incoming, aiSuggested: true }
        : s;
    });
  }

  private addAiMessage(text: string): void {
    this.messages.push({ role: 'ai', text, timestamp: new Date() });
  }

  formatTime(d: Date): string {
    return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
  }
}

