import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

const ROLE_DASHBOARD: Record<string, string> = {
  patient: '/patient/dashboard',
  staff:   '/staff/schedule',
  admin:   '/admin/users'
};

export const roleRedirectGuard: CanActivateFn = (_route, _state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  const role = auth.getCurrentRole();
  const target = role ? ROLE_DASHBOARD[role] : null;

  if (target) {
    return router.createUrlTree([target]);
  }

  return router.createUrlTree(['/login'], { queryParams: { reason: 'unauthorized' } });
};
