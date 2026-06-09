import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AuthService, UserRole } from '../services/auth.service';

export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot, _state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  const allowedRoles = route.data['roles'] as UserRole[] | undefined;
  const userRole     = auth.getCurrentRole();

  if (!allowedRoles || allowedRoles.length === 0) { return true; }

  if (!userRole || !allowedRoles.includes(userRole)) {
    console.warn('[RoleGuard] Cross-role access blocked', {
      route:    route.url.map(s => s.path).join('/'),
      userRole: userRole ?? 'none',
      required: allowedRoles
    });
    return router.createUrlTree(['/login'], { queryParams: { reason: 'unauthorized' } });
  }

  return true;
};
