import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlSegment } from '@angular/router';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { roleGuard } from './role.guard';
import { AuthService } from '../services/auth.service';

function makeToken(role: string, expired = false): string {
  const exp = expired
    ? Math.floor(Date.now() / 1000) - 60
    : Math.floor(Date.now() / 1000) + 900;
  return `${btoa('{}')}.${btoa(JSON.stringify({ role, exp }))}.sig`;
}

function makeRoute(roles: string[], urlPath: string): ActivatedRouteSnapshot {
  const route = new ActivatedRouteSnapshot();
  (route as unknown as { data: object }).data = { roles };
  (route as unknown as { url: UrlSegment[] }).url = [new UrlSegment(urlPath, {})];
  return route;
}

describe('roleGuard', () => {
  const dummyState = {} as RouterStateSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [{ provide: Router, useValue: { createUrlTree: (cmds: string[], extras: object) => ({ commands: cmds, ...extras }) } }]
    });
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  it('returns true when patient accesses /patient route', () => {
    localStorage.setItem('access_token', makeToken('patient'));
    const route = makeRoute(['patient'], 'patient/dashboard');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState));
    expect(result).toBeTrue();
  });

  it('blocks patient from accessing /staff route and logs warning (TC-005)', () => {
    localStorage.setItem('access_token', makeToken('patient'));
    const warnSpy = spyOn(console, 'warn');
    const route = makeRoute(['staff'], 'staff/schedule');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState)) as unknown as { commands: string[] };
    expect(result.commands).toEqual(['/login']);
    expect(warnSpy).toHaveBeenCalledWith(
      '[RoleGuard] Cross-role access blocked',
      jasmine.objectContaining({ userRole: 'patient', required: ['staff'] })
    );
  });

  it('blocks staff from accessing /patient route', () => {
    localStorage.setItem('access_token', makeToken('staff'));
    const route = makeRoute(['patient'], 'patient/dashboard');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState)) as unknown as { commands: string[] };
    expect(result.commands).toEqual(['/login']);
  });

  it('returns true when staff accesses /staff route', () => {
    localStorage.setItem('access_token', makeToken('staff'));
    const route = makeRoute(['staff'], 'staff/schedule');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState));
    expect(result).toBeTrue();
  });

  it('returns true when admin accesses /admin route', () => {
    localStorage.setItem('access_token', makeToken('admin'));
    const route = makeRoute(['admin'], 'admin/users');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState));
    expect(result).toBeTrue();
  });

  it('returns true when no roles restriction on route (TC-006: empty array)', () => {
    localStorage.setItem('access_token', makeToken('patient'));
    const route = makeRoute([], 'login');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState));
    expect(result).toBeTrue();
  });

  it('returns true when route.data.roles is undefined (TC-006: undefined branch)', () => {
    localStorage.setItem('access_token', makeToken('patient'));
    const route = new ActivatedRouteSnapshot();
    (route as unknown as { data: object }).data = {};  // no roles key → allowedRoles = undefined
    (route as unknown as { url: UrlSegment[] }).url = [];
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState));
    expect(result).toBeTrue();
  });

  it('redirects when user has no token and route requires a role', () => {
    // No token → getCurrentRole() returns null → !userRole branch
    spyOn(console, 'warn');
    const route = makeRoute(['patient'], 'patient/dashboard');
    const result = TestBed.runInInjectionContext(() => roleGuard(route, dummyState)) as unknown as { commands: string[] };
    expect(result.commands).toEqual(['/login']);
  });
});
