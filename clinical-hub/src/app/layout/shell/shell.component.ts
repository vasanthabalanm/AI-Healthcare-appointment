import { Component, computed, inject } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet, Router } from '@angular/router';
import { AuthService, UserRole } from '../../core/services/auth.service';

interface NavItem {
  label: string;
  route: string;
  icon: string; // SVG path data
}

// Nav items extracted verbatim from wireframes SCR-005, SCR-016, SCR-022
const PATIENT_NAV: NavItem[] = [
  { label: 'Dashboard',        route: '/patient/dashboard',    icon: 'M10.707 2.293a1 1 0 00-1.414 0l-7 7a1 1 0 001.414 1.414L4 10.414V17a1 1 0 001 1h2a1 1 0 001-1v-2a1 1 0 011-1h2a1 1 0 011 1v2a1 1 0 001 1h2a1 1 0 001-1v-6.586l.293.293a1 1 0 001.414-1.414l-7-7z' },
  { label: 'Book Appointment', route: '/patient/book',         icon: 'M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z' },
  { label: 'My Appointments',  route: '/patient/appointments', icon: 'M3 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1z' },
  { label: 'My Intake',        route: '/patient/intake',       icon: 'M18 10c0 3.866-3.582 7-8 7a8.841 8.841 0 01-4.083-.98L2 17l1.338-3.123C2.493 12.767 2 11.434 2 10c0-3.866 3.582-7 8-7s8 3.134 8 7zM7 9H5v2h2V9zm8 0h-2v2h2V9zM9 9h2v2H9V9z' },
  { label: 'My Documents',     route: '/patient/documents',    icon: 'M4 4a2 2 0 012-2h4.586A2 2 0 0112 2.586L15.414 6A2 2 0 0116 7.414V16a2 2 0 01-2 2H6a2 2 0 01-2-2V4zm2 6a1 1 0 011-1h6a1 1 0 110 2H7a1 1 0 01-1-1zm1 3a1 1 0 100 2h6a1 1 0 100-2H7z' }
];

const STAFF_NAV: NavItem[] = [
  { label: 'Daily Schedule',     route: '/staff/schedule', icon: 'M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z' },
  { label: 'Walk-In Registration', route: '/staff/walkin', icon: 'M10 3a1 1 0 011 1v5h5a1 1 0 110 2h-5v5a1 1 0 11-2 0v-5H4a1 1 0 110-2h5V4a1 1 0 011-1z' },
  { label: 'Same-Day Queue',     route: '/staff/queue',    icon: 'M3 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1z' },
  { label: 'Code Verification',  route: '/staff/coding',   icon: 'M9 2a1 1 0 000 2h2a1 1 0 100-2H9z M4 5a2 2 0 012-2 3 3 0 006 0 2 2 0 012 2v11a2 2 0 01-2 2H6a2 2 0 01-2-2V5zm3 4a1 1 0 000 2h.01a1 1 0 100-2H7zm3 0a1 1 0 000 2h3a1 1 0 100-2h-3zm-3 4a1 1 0 100 2h.01a1 1 0 100-2H7zm3 0a1 1 0 100 2h3a1 1 0 100-2h-3z' }
];

const ADMIN_NAV: NavItem[] = [
  { label: 'User Accounts', route: '/admin/users', icon: 'M9 6a3 3 0 11-6 0 3 3 0 016 0zM17 6a3 3 0 11-6 0 3 3 0 016 0zM12.93 17c.046-.327.07-.66.07-1a6.97 6.97 0 00-1.5-4.33A5 5 0 0119 16v1h-6.07zM6 11a5 5 0 015 5v1H1v-1a5 5 0 015-5z' },
  { label: 'Audit Log',     route: '/admin/audit',  icon: 'M3 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm0 4a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1z' }
];

const NAV_MAP: Record<UserRole, NavItem[]> = {
  patient: PATIENT_NAV,
  staff:   STAFF_NAV,
  admin:   ADMIN_NAV
};

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TitleCasePipe],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);

  readonly navItems = computed(() => {
    const role = this.auth.getCurrentRole();
    return role ? NAV_MAP[role] : [];
  });

  readonly currentRole = computed(() => this.auth.getCurrentRole());

  readonly userInitials = computed(() => {
    const token = this.auth.getToken();
    if (!token) { return '?'; }
    const payload = this.auth.decodeToken(token);
    const first = ((payload?.['given_name']  as string | undefined) ?? '')[0] ?? '';
    const last  = ((payload?.['family_name'] as string | undefined) ?? '')[0] ?? '';
    return (first + last).toUpperCase() || '?';
  });

  readonly userName = computed(() => {
    const token = this.auth.getToken();
    if (!token) { return ''; }
    const payload = this.auth.decodeToken(token);
    const first = (payload?.['given_name']  as string | undefined) ?? '';
    const last  = (payload?.['family_name'] as string | undefined) ?? '';
    return `${first} ${last}`.trim();
  });

  signOut(): void {
    // Call server logout to revoke the Redis allowlist entry (TASK_015 AC-004),
    // then clear the local token and navigate to login.
    this.auth.logout().subscribe({
      next:  () => void this.router.navigate(['/login']),
      error: () => {
        // Even if the server call fails (Redis down), clear the local token.
        this.auth.clearToken();
        void this.router.navigate(['/login']);
      }
    });
  }
}
