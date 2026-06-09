import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { roleRedirectGuard } from './role-redirect.guard';
import { AuthService } from '../services/auth.service';

function makeToken(role: string): string {
  const exp = Math.floor(Date.now() / 1000) + 900;
  return `${btoa('{}')}.${btoa(JSON.stringify({ role, exp }))}.sig`;
}

describe('roleRedirectGuard', () => {
  const dummyRoute = {} as ActivatedRouteSnapshot;
  const dummyState = {} as RouterStateSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [{
        provide: Router,
        useValue: { createUrlTree: (cmds: string[], extras?: object) => ({ commands: cmds, ...(extras ?? {}) }) }
      }]
    });
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  // TC-007: patient role → /patient/dashboard
  it('returns UrlTree for /patient/dashboard when role is patient', () => {
    localStorage.setItem('access_token', makeToken('patient'));
    const result = TestBed.runInInjectionContext(() => roleRedirectGuard(dummyRoute, dummyState)) as unknown as { commands: string[] };
    expect(result.commands).toEqual(['/patient/dashboard']);
  });

  // staff role → /staff/schedule
  it('returns UrlTree for /staff/schedule when role is staff', () => {
    localStorage.setItem('access_token', makeToken('staff'));
    const result = TestBed.runInInjectionContext(() => roleRedirectGuard(dummyRoute, dummyState)) as unknown as { commands: string[] };
    expect(result.commands).toEqual(['/staff/schedule']);
  });

  // admin role → /admin/users
  it('returns UrlTree for /admin/users when role is admin', () => {
    localStorage.setItem('access_token', makeToken('admin'));
    const result = TestBed.runInInjectionContext(() => roleRedirectGuard(dummyRoute, dummyState)) as unknown as { commands: string[] };
    expect(result.commands).toEqual(['/admin/users']);
  });

  // TC-008: null role → /login?reason=unauthorized
  it('redirects to /login?reason=unauthorized when no token is present', () => {
    const result = TestBed.runInInjectionContext(() => roleRedirectGuard(dummyRoute, dummyState)) as unknown as { commands: string[]; queryParams: { reason: string } };
    expect(result.commands).toEqual(['/login']);
    expect(result['queryParams']?.['reason']).toBe('unauthorized');
  });
});
