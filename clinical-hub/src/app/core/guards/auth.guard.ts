import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, _state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  const token = auth.getToken();

  if (!token) {
    return router.createUrlTree(['/login'], { queryParams: { reason: 'unauthorized' } });
  }

  if (auth.isTokenExpired(token)) {
    auth.clearToken();
    return router.createUrlTree(['/login'], { queryParams: { reason: 'timeout' } });
  }

  return true;
};
