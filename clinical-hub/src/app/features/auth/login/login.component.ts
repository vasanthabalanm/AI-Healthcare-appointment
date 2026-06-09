import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route  = inject(ActivatedRoute);

  readonly reason  = signal<string | null>(this.route.snapshot.queryParamMap.get('reason'));
  readonly error   = signal<string | null>(null);
  readonly loading = signal(false);

  readonly form = this.fb.nonNullable.group({
    email:    ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  submit(): void {
    if (this.form.invalid) { return; }
    this.error.set(null);
    this.loading.set(true);

    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => {
        this.loading.set(false);
        const role   = this.auth.getCurrentRole();
        const target = role === 'staff' ? '/staff/schedule'
                     : role === 'admin' ? '/admin/users'
                     : '/patient/dashboard';
        void this.router.navigate([target]);
      },
      error: (err: { status?: number }) => {
        this.loading.set(false);
        this.form.controls.password.reset();
        if (err?.status === 423) {
          this.error.set('Your account is temporarily locked due to too many failed login attempts. Please try again in 15 minutes.');
        } else {
          this.error.set('The email or password you entered is incorrect. Please try again.');
        }
      }
    });
  }
}
