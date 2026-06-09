import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';

function confirmPasswordValidator(control: AbstractControl): ValidationErrors | null {
  const parent = control.parent;
  if (!parent) { return null; }
  const password = parent.get('password')?.value;
  return control.value === password ? null : { mismatch: true };
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <!-- Success overlay -->
    <div class="overlay" *ngIf="success">
      <div class="overlay-card">
        <div class="checkmark-circle">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="var(--cs0)" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="20 6 9 17 4 12"/>
          </svg>
        </div>
        <h2 class="overlay-title">Check your inbox</h2>
        <p class="overlay-body">We sent a verification link to<br><strong>{{ submittedEmail }}</strong></p>
        <a routerLink="/login" class="btn btn--primary overlay-btn">Back to sign in</a>
      </div>
    </div>

    <!-- Registration card -->
    <div class="page-center" *ngIf="!success">
      <div class="card">
        <h1 class="card-title">Create your account</h1>
        <p class="card-subtitle">Join ClinicalHub to manage your healthcare appointments.</p>

        <!-- Error summary (409 duplicate) -->
        <div class="error-summary" *ngIf="serverError">
          <ul>
            <li>{{ serverError }}</li>
          </ul>
        </div>

        <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
          <!-- Personal info section -->
          <div class="section-title">Personal Information</div>

          <div class="row-2col">
            <div class="form-group">
              <label class="form-label" for="firstName">First name <span class="req">*</span></label>
              <input id="firstName" type="text" class="form-input"
                [class.form-input--error]="isInvalid('firstName')"
                formControlName="firstName" autocomplete="given-name">
              <span class="form-error" *ngIf="isInvalid('firstName')">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                First name is required.
              </span>
            </div>
            <div class="form-group">
              <label class="form-label" for="lastName">Last name <span class="req">*</span></label>
              <input id="lastName" type="text" class="form-input"
                [class.form-input--error]="isInvalid('lastName')"
                formControlName="lastName" autocomplete="family-name">
              <span class="form-error" *ngIf="isInvalid('lastName')">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                Last name is required.
              </span>
            </div>
          </div>

          <div class="row-2col">
            <div class="form-group">
              <label class="form-label" for="dob">Date of birth <span class="req">*</span></label>
              <input id="dob" type="date" class="form-input"
                [class.form-input--error]="isInvalid('dob')"
                formControlName="dob" autocomplete="bday">
              <span class="form-error" *ngIf="isInvalid('dob')">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                Date of birth is required.
              </span>
            </div>
            <div class="form-group">
              <label class="form-label" for="phone">Phone number</label>
              <input id="phone" type="tel" class="form-input"
                formControlName="phone" autocomplete="tel" placeholder="+1 (555) 000-0000">
            </div>
          </div>

          <!-- Account credentials section -->
          <div class="section-title">Account Credentials</div>

          <div class="form-group">
            <label class="form-label" for="email">Email address <span class="req">*</span></label>
            <input id="email" type="email" class="form-input"
              [class.form-input--error]="isInvalid('email')"
              formControlName="email" autocomplete="email">
            <span class="form-error" *ngIf="isInvalid('email')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
              <span *ngIf="form.get('email')?.hasError('required')">Email address is required.</span>
              <span *ngIf="form.get('email')?.hasError('email')">Enter a valid email address.</span>
            </span>
          </div>

          <div class="form-group">
            <label class="form-label" for="password">Password <span class="req">*</span></label>
            <input id="password" type="password" class="form-input"
              [class.form-input--error]="isInvalid('password')"
              formControlName="password" autocomplete="new-password"
              (input)="onPasswordInput()">
            <!-- Strength meter -->
            <div class="strength-meter" *ngIf="form.get('password')?.value">
              <div class="strength-bars">
                <div class="strength-bar" [class]="strengthBarClass(1)"></div>
                <div class="strength-bar" [class]="strengthBarClass(2)"></div>
                <div class="strength-bar" [class]="strengthBarClass(3)"></div>
                <div class="strength-bar" [class]="strengthBarClass(4)"></div>
              </div>
              <span class="strength-label" [class]="'strength-label--' + strengthLevel">{{ strengthLabel }}</span>
            </div>
            <span class="form-error" *ngIf="isInvalid('password')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
              Password must be at least 8 characters.
            </span>
          </div>

          <div class="form-group">
            <label class="form-label" for="confirmPassword">Confirm password <span class="req">*</span></label>
            <input id="confirmPassword" type="password" class="form-input"
              [class.form-input--error]="isInvalid('confirmPassword')"
              formControlName="confirmPassword" autocomplete="new-password">
            <span class="form-error" *ngIf="isInvalid('confirmPassword')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
              Passwords do not match.
            </span>
          </div>

          <button type="submit" class="btn btn--primary btn--full" [disabled]="loading">
            <span *ngIf="!loading">Create account</span>
            <span *ngIf="loading">Creating account&hellip;</span>
          </button>
        </form>

        <p class="card-footer">Already have an account? <a routerLink="/login">Sign in</a></p>
      </div>
    </div>
  `,
  styles: [`
    .page-center {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--cs1);
      padding: var(--sp6);
    }
    .card {
      background: var(--cs0);
      border-radius: var(--r3);
      padding: var(--sp8);
      width: 100%;
      max-width: 560px;
      box-shadow: 0 2px 12px rgba(0,0,0,.08);
    }
    .card-title { font-size: 24px; font-weight: 600; color: var(--ct1); margin: 0 0 var(--sp2); }
    .card-subtitle { color: var(--ct2); font-size: 14px; margin: 0 0 var(--sp6); }
    .section-title {
      font-size: 12px; font-weight: 600; text-transform: uppercase;
      letter-spacing: .06em; color: var(--ct2);
      border-top: 1px solid var(--cb);
      padding-top: var(--sp4);
      margin: var(--sp6) 0 var(--sp4);
    }
    .row-2col { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp4); }
    .form-group { display: flex; flex-direction: column; gap: 4px; margin-bottom: var(--sp4); }
    .form-label { font-size: 13px; font-weight: 500; color: var(--ct1); }
    .req { color: var(--ce); }
    .form-input {
      height: 40px; border: 1px solid var(--cb); border-radius: var(--r2);
      padding: 0 var(--sp3); font-size: 14px; color: var(--ct1);
      background: var(--cs0); outline: none; transition: border-color .15s;
    }
    .form-input:focus { border-color: var(--cp); box-shadow: 0 0 0 3px var(--cps); }
    .form-input--error { border-color: var(--ce); }
    .form-input--error:focus { box-shadow: 0 0 0 3px var(--ceb); }
    .form-error { display: flex; align-items: center; gap: 4px; font-size: 12px; color: var(--ce); margin-top: 2px; }
    .error-summary {
      background: var(--ceb); border: 1px solid var(--ce); border-radius: var(--r2);
      padding: var(--sp3) var(--sp4); margin-bottom: var(--sp4); font-size: 14px; color: var(--ce);
    }
    .error-summary ul { margin: 0; padding-left: var(--sp4); }
    .strength-meter { display: flex; align-items: center; gap: var(--sp3); margin-top: var(--sp2); }
    .strength-bars { display: flex; gap: 4px; }
    .strength-bar { width: 40px; height: 4px; border-radius: 2px; background: var(--cb); transition: background .2s; }
    .strength-bar.filled-weak { background: var(--ce); }
    .strength-bar.filled-ok { background: var(--cw); }
    .strength-bar.filled-strong { background: var(--cok); }
    .strength-label { font-size: 12px; color: var(--ct2); }
    .strength-label--weak { color: var(--ce); }
    .strength-label--ok { color: var(--cw); }
    .strength-label--strong { color: var(--cok); }
    .btn {
      display: inline-flex; align-items: center; justify-content: center;
      height: 40px; padding: 0 var(--sp5); border: none; border-radius: var(--r2);
      font-size: 14px; font-weight: 500; cursor: pointer; transition: background .15s;
    }
    .btn--primary { background: var(--cp); color: var(--cti); }
    .btn--primary:hover:not(:disabled) { background: var(--cph); }
    .btn--primary:disabled { opacity: .6; cursor: not-allowed; }
    .btn--full { width: 100%; margin-top: var(--sp2); }
    .card-footer { text-align: center; font-size: 14px; color: var(--ct2); margin-top: var(--sp5); }
    .card-footer a { color: var(--cp); text-decoration: none; font-weight: 500; }
    .card-footer a:hover { text-decoration: underline; }
    .overlay {
      position: fixed; inset: 0; background: rgba(0,0,0,.45);
      display: flex; align-items: center; justify-content: center; z-index: 200;
    }
    .overlay-card {
      background: var(--cs0); border-radius: var(--r3); padding: var(--sp8);
      max-width: 400px; width: 100%; text-align: center;
      box-shadow: 0 8px 32px rgba(0,0,0,.18);
    }
    .checkmark-circle {
      width: 64px; height: 64px; background: var(--cok); border-radius: 50%;
      display: flex; align-items: center; justify-content: center; margin: 0 auto var(--sp5);
    }
    .overlay-title { font-size: 20px; font-weight: 600; color: var(--ct1); margin: 0 0 var(--sp3); }
    .overlay-body { font-size: 14px; color: var(--ct2); margin: 0 0 var(--sp6); line-height: 1.6; }
    .overlay-btn { width: 100%; }
  `]
})
export class RegisterComponent implements OnInit {
  form!: FormGroup;
  loading = false;
  success = false;
  submittedEmail = '';
  serverError: string | null = null;
  strengthScore = 0;

  constructor(private fb: FormBuilder, private authService: AuthService) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      firstName:       ['', [Validators.required, Validators.maxLength(100)]],
      lastName:        ['', [Validators.required, Validators.maxLength(100)]],
      dob:             ['', Validators.required],
      phone:           [''],
      email:           ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
      password:        ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required, confirmPasswordValidator]]
    });

    this.form.get('password')?.valueChanges.subscribe(() => {
      this.form.get('confirmPassword')?.updateValueAndValidity();
    });
  }

  isInvalid(field: string): boolean {
    const ctrl = this.form.get(field);
    return !!(ctrl?.invalid && ctrl.touched);
  }

  onPasswordInput(): void {
    const val: string = this.form.get('password')?.value ?? '';
    this.strengthScore = this.checkStrength(val);
  }

  checkStrength(v: string): number {
    let score = 0;
    if (v.length >= 8) { score++; }
    if (/\d/.test(v)) { score++; }
    if (/[^A-Za-z0-9]/.test(v)) { score++; }
    if (v.length >= 12) { score++; }
    return score;
  }

  get strengthLevel(): 'weak' | 'ok' | 'strong' | '' {
    if (this.strengthScore === 0) { return ''; }
    if (this.strengthScore <= 1) { return 'weak'; }
    if (this.strengthScore <= 2) { return 'ok'; }
    return 'strong';
  }

  get strengthLabel(): string {
    const map: Record<string, string> = { weak: 'Weak', ok: 'Fair', strong: 'Strong' };
    return this.strengthLevel ? map[this.strengthLevel] : '';
  }

  strengthBarClass(barIndex: number): string {
    if (this.strengthScore === 0 || barIndex > this.strengthScore) { return ''; }
    return `filled-${this.strengthLevel}`;
  }

  onSubmit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) { return; }
    this.serverError = null;
    this.loading = true;

    const { email, password, firstName, lastName } = this.form.value as {
      email: string; password: string; firstName: string; lastName: string;
    };

    this.authService.register({ email, password, firstName, lastName }).subscribe({
      next: () => {
        this.submittedEmail = email;
        this.loading = false;
        this.success = true;
      },
      error: (err: HttpErrorResponse) => {
        this.loading = false;
        if (err.status === 409) {
          this.serverError = (err.error as { error?: string })?.error ?? 'An account with this email already exists.';
        } else if (err.status === 422) {
          const errs = (err.error as { errors?: Record<string, string[]> })?.errors;
          this.serverError = errs
            ? Object.values(errs).flat().join(' ')
            : 'Please check the form and try again.';
        } else {
          this.serverError = 'Something went wrong. Please try again.';
        }
      }
    });
  }
}
