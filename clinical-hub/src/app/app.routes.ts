import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';
import { roleRedirectGuard } from './core/guards/role-redirect.guard';

export const routes: Routes = [
  // Public
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'forgot-password',
    loadComponent: () => import('./features/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./features/auth/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
  },
  {
    path: 'setup-credentials',
    loadComponent: () => import('./features/auth/setup-credentials/setup-credentials.component').then(m => m.SetupCredentialsComponent)
  },

  // Patient routes
  {
    path: 'patient',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['patient'] },
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: 'dashboard',     loadComponent: () => import('./features/patient/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'book',          loadComponent: () => import('./features/patient/book/book.component').then(m => m.BookComponent) },
      { path: 'appointments',  loadComponent: () => import('./features/patient/appointments/appointments.component').then(m => m.AppointmentsComponent) },
      { path: 'waitlist',      loadComponent: () => import('./features/patient/waitlist/waitlist.component').then(m => m.WaitlistComponent) },
      { path: 'intake',        loadComponent: () => import('./features/patient/intake/intake.component').then(m => m.IntakeComponent) },
      { path: 'intake/manual', loadComponent: () => import('./features/patient/intake/manual/manual-intake.component').then(m => m.ManualIntakeComponent) },
      { path: 'documents',     loadComponent: () => import('./features/patient/documents/documents.component').then(m => m.DocumentsComponent) },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  // Staff routes
  {
    path: 'staff',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['staff'] },
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: 'schedule', loadComponent: () => import('./features/staff/schedule/schedule.component').then(m => m.ScheduleComponent) },
      { path: 'walkin',   loadComponent: () => import('./features/staff/walkin/walkin.component').then(m => m.WalkinComponent) },
      { path: 'queue',    loadComponent: () => import('./features/staff/queue/queue.component').then(m => m.QueueComponent) },
      { path: 'coding',   loadComponent: () => import('./features/staff/coding/coding.component').then(m => m.CodingComponent) },
      { path: 'patients/:id/view360', loadComponent: () => import('./features/staff/patient360/patient360.component').then(m => m.Patient360Component) },
      { path: '', redirectTo: 'schedule', pathMatch: 'full' }
    ]
  },

  // Admin routes
  {
    path: 'admin',
    canActivate: [authGuard, roleGuard],
    data: { roles: ['admin'] },
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: 'users', loadComponent: () => import('./features/admin/users/users.component').then(m => m.UsersComponent) },
      { path: 'audit', loadComponent: () => import('./features/admin/audit/audit.component').then(m => m.AuditComponent) },
      { path: '', redirectTo: 'users', pathMatch: 'full' }
    ]
  },

  // Root redirect
  { path: '', canActivate: [roleRedirectGuard], children: [] },

  // Wildcard — redirect to role dashboard or login
  { path: '**', canActivate: [roleRedirectGuard], children: [] }
];
