import { Component, inject, signal, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

function passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
  const newPassword     = control.get('newPassword')?.value as string;
  const confirmPassword = control.get('confirmPassword')?.value as string;
  return newPassword === confirmPassword ? null : { passwordMismatch: true };
}

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss'
})
export class ResetPasswordComponent implements OnInit {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route  = inject(ActivatedRoute);

  readonly error   = signal<string | null>(null);
  readonly loading = signal(false);

  private email = '';
  private token = '';

  readonly form = this.fb.nonNullable.group(
    {
      newPassword:     ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required]
    },
    { validators: passwordMatchValidator }
  );

  ngOnInit(): void {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';

    if (!this.email || !this.token) {
      void this.router.navigate(['/forgot-password']);
    }
  }

  submit(): void {
    if (this.form.invalid) { return; }
    this.error.set(null);
    this.loading.set(true);

    this.auth.resetPassword(this.email, this.token, this.form.controls.newPassword.value).subscribe({
      next: () => {
        this.loading.set(false);
        void this.router.navigate(['/login'], { queryParams: { reason: 'password-reset' } });
      },
      error: (err: { status?: number }) => {
        this.loading.set(false);
        if (err.status === 400) {
          this.error.set('This reset link is invalid or has expired. Please request a new one.');
        } else if (err.status === 422) {
          this.error.set('Password must be at least 8 characters.');
        } else {
          this.error.set('Something went wrong. Please try again.');
        }
      }
    });
  }
}
