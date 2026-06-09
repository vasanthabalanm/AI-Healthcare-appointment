import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss'
})
export class ForgotPasswordComponent {
  private readonly fb   = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly submitted = signal(false);
  readonly error     = signal<string | null>(null);
  readonly loading   = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

  submit(): void {
    if (this.form.invalid) { return; }
    this.error.set(null);
    this.loading.set(true);

    this.auth.forgotPassword(this.form.controls.email.value).subscribe({
      next: () => {
        this.loading.set(false);
        this.submitted.set(true);
      },
      error: () => {
        this.loading.set(false);
        // AC-001: backend always returns 200 — this branch only fires on network errors.
        this.error.set('Unable to send reset email. Please try again later.');
      }
    });
  }
}
