import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

/**
 * Attaches the stored JWT as a Bearer token on all outgoing HTTP requests.
 * No-ops when no token is present (public routes such as /api/auth/login).
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).getToken();
  if (!token) { return next(req); }

  const authorised = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` }
  });
  return next(authorised);
};
