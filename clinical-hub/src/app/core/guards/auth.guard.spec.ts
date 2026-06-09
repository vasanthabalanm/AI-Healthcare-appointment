import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

function makeToken(payload: object, expired = false): string {
  const exp = expired
    ? Math.floor(Date.now() / 1000) - 60
    : Math.floor(Date.now() / 1000) + 900;
  const full = { ...payload, exp };
  return `${btoa('{}')}.${btoa(JSON.stringify(full))}.sig`;
}

describe('authGuard', () => {
  let auth: AuthService;
  let router: Router;
  const dummyRoute = {} as ActivatedRouteSnapshot;
  const dummyState = {} as RouterStateSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [{ provide: Router, useValue: { createUrlTree: (cmds: string[], extras: object) => ({ commands: cmds, ...extras }) } }]
    });
    auth   = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  it('redirects to /login?reason=unauthorized when no token', () => {
    const result = TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState)) as UrlTree;
    expect((result as unknown as { commands: string[] }).commands).toEqual(['/login']);
  });

  it('redirects to /login?reason=timeout for expired token and clears token (TC-003)', () => {
    localStorage.setItem('access_token', makeToken({ role: 'patient' }, true));
    const clearSpy = spyOn(auth, 'clearToken').and.callThrough();
    const result = TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState));
    expect((result as unknown as { queryParams: { reason: string } }).queryParams?.['reason']).toBe('timeout');
    expect(clearSpy).toHaveBeenCalledTimes(1);
  });

  it('returns true for valid non-expired token', () => {
    localStorage.setItem('access_token', makeToken({ role: 'patient' }));
    const result = TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState));
    expect(result).toBeTrue();
  });

  it('handles empty JWT payload with valid signature without throwing', () => {
    // Token with valid structure but empty payload (no exp)
    const token = `${btoa('{}')}.${btoa('{}')}.sig`;
    localStorage.setItem('access_token', token);
    expect(() => TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState))).not.toThrow();
  });
});
