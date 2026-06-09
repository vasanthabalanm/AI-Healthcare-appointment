import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, OperatorFunction, catchError, of, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Session lifecycle interceptor (TASK_015).
 *
 * On every outgoing request that carries a token:
 *   - If the token has <= 120 seconds remaining, call POST /auth/extend-session
 *     proactively to refresh the Redis TTL and rotate the token.
 *
 * On every response:
 *   - 401 -> token revoked (Redis allowlist miss) or expired: clear token and
 *     redirect to /login?reason=timeout.
 *   - 423 -> account locked: clear token and redirect to /login?reason=locked.
 */
export const sessionInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  // Skip public endpoints that do not carry a token to avoid loops.
  const isPublic = req.url.includes('/auth/login')
    || req.url.includes('/auth/register')
    || req.url.includes('/auth/extend-session')
    || req.url.includes('/auth/forgot-password')
    || req.url.includes('/auth/reset-password')
    || req.url.includes('/auth/setup-credentials');

  const token = auth.getToken();

  if (!isPublic && token && shouldRefresh(token, auth)) {
    return auth.extendSession().pipe(
      catchError(() => of(null)),
      switchMap((): Observable<HttpEvent<unknown>> =>
        next(req).pipe(sessionErrorHandler(auth, router))
      )
    );
  }

  return next(req).pipe(sessionErrorHandler(auth, router));
};

function shouldRefresh(token: string, auth: AuthService): boolean {
  const payload = auth.decodeToken(token);
  if (!payload || typeof payload['exp'] !== 'number') { return false; }
  const secondsLeft = (payload['exp'] as number) - Math.floor(Date.now() / 1000);
  return secondsLeft > 0 && secondsLeft <= 120;
}

function sessionErrorHandler(
  auth: AuthService,
  router: Router
): OperatorFunction<HttpEvent<unknown>, HttpEvent<unknown>> {
  return catchError<HttpEvent<unknown>, Observable<never>>(
    (err: { status?: number }) => {
      if (err?.status === 401) {
        auth.clearToken();
        void router.navigate(['/login'], { queryParams: { reason: 'timeout' } });
      }
      if (err?.status === 423) {
        auth.clearToken();
        void router.navigate(['/login'], { queryParams: { reason: 'locked' } });
      }
      return throwError(() => err);
    }
  );
}
