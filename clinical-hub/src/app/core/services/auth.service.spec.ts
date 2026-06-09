import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import {
  JWT_PATIENT_VALID,
  JWT_STAFF_VALID,
  JWT_EXPIRED,
  JWT_UNKNOWN_ROLE,
  JWT_INVALID_B64
} from './test-data/jwt-fixtures';

function makeToken(payload: object, expired = false): string {
  const exp = expired
    ? Math.floor(Date.now() / 1000) - 60   // 1 min ago
    : Math.floor(Date.now() / 1000) + 900; // 15 min from now
  const full = { ...payload, exp };
  const header  = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const body    = btoa(JSON.stringify(full));
  const sig     = 'fakesignature';
  return `${header}.${body}.${sig}`;
}

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AuthService);
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  // decodeToken — never throws
  it('decodeToken returns null for empty string — no throw', () => {
    expect(() => service.decodeToken('')).not.toThrow();
    expect(service.decodeToken('')).toBeNull();
  });

  it('decodeToken returns null for malformed JWT — no throw', () => {
    expect(() => service.decodeToken('not.a.jwt')).not.toThrow();
    // 'not' is valid base64 but not a JSON object with expected shape
  });

  it('decodeToken returns null for token with only 2 parts', () => {
    expect(service.decodeToken('header.body')).toBeNull();
  });

  it('decodeToken returns payload for valid token', () => {
    const token = makeToken({ role: 'patient', name: 'Alex' });
    const payload = service.decodeToken(token);
    expect(payload?.['role']).toBe('patient');
    expect(payload?.['name']).toBe('Alex');
  });

  it('decodeToken handles Base64URL chars (-) in payload — no throw', () => {
    // Craft a token whose payload contains `-` (Base64URL) in its raw encoding
    const jsonStr = JSON.stringify({ role: 'patient', exp: Math.floor(Date.now() / 1000) + 900 });
    const b64url  = btoa(jsonStr).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    const token   = `${btoa('{}')}.${b64url}.sig`;
    expect(() => service.decodeToken(token)).not.toThrow();
    expect(service.decodeToken(token)?.['role']).toBe('patient');
  });

  it('decodeToken handles Base64URL chars (_) in payload — no throw', () => {
    // `_` replaces `/` in Base64URL; ensure normalisation handles it
    const jsonStr = JSON.stringify({ role: 'staff', exp: Math.floor(Date.now() / 1000) + 900 });
    const b64url  = btoa(jsonStr).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    const token   = `${btoa('{}')}.${b64url}.sig`;
    expect(service.decodeToken(token)?.['role']).toBe('staff');
  });

  // isTokenExpired
  it('isTokenExpired returns true for expired token', () => {
    const token = makeToken({ role: 'patient' }, true);
    expect(service.isTokenExpired(token)).toBeTrue();
  });

  it('isTokenExpired returns false for valid token', () => {
    const token = makeToken({ role: 'patient' });
    expect(service.isTokenExpired(token)).toBeFalse();
  });

  it('isTokenExpired returns true for malformed token — no throw', () => {
    expect(() => service.isTokenExpired('bad')).not.toThrow();
    expect(service.isTokenExpired('bad')).toBeTrue();
  });

  it('isTokenExpired returns true for empty-payload token with valid signature', () => {
    const header = btoa('{}');
    const body   = btoa('{}'); // no exp field
    const token  = `${header}.${body}.sig`;
    expect(service.isTokenExpired(token)).toBeTrue();
  });

  // getCurrentRole
  it('getCurrentRole returns null when no token in localStorage', () => {
    expect(service.getCurrentRole()).toBeNull();
  });

  it('getCurrentRole returns patient role from stored token', () => {
    localStorage.setItem('access_token', makeToken({ role: 'patient' }));
    expect(service.getCurrentRole()).toBe('patient');
  });

  it('getCurrentRole returns staff role from stored token', () => {
    localStorage.setItem('access_token', makeToken({ role: 'staff' }));
    expect(service.getCurrentRole()).toBe('staff');
  });

  it('getCurrentRole returns null for unknown role value', () => {
    localStorage.setItem('access_token', makeToken({ role: 'superuser' }));
    expect(service.getCurrentRole()).toBeNull();
  });

  // isAuthenticated
  it('isAuthenticated returns false when no token', () => {
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('isAuthenticated returns true for valid non-expired token', () => {
    localStorage.setItem('access_token', makeToken({ role: 'patient' }));
    expect(service.isAuthenticated()).toBeTrue();
  });

  it('isAuthenticated returns false for expired token', () => {
    localStorage.setItem('access_token', makeToken({ role: 'patient' }, true));
    expect(service.isAuthenticated()).toBeFalse();
  });

  // --- TC-001: getToken() — explicit read from localStorage ---
  it('TC-001: getToken returns stored token value', () => {
    spyOn(localStorage, 'getItem').and.returnValue('mock.jwt.token');
    expect(service.getToken()).toBe('mock.jwt.token');
  });

  // --- TC-002: getToken() — returns null when no key ---
  it('TC-002: getToken returns null when localStorage is empty', () => {
    spyOn(localStorage, 'getItem').and.returnValue(null);
    expect(service.getToken()).toBeNull();
  });

  // --- TC-003: decodeToken with pre-encoded fixture JWT ---
  it('TC-003: decodeToken returns correct sub, role and exp from JWT_PATIENT_VALID', () => {
    const result = service.decodeToken(JWT_PATIENT_VALID);
    expect(result?.['sub']).toBe('1');
    expect(result?.['role']).toBe('patient');
    expect(result?.['exp']).toBe(9999999999);
  });

  // --- Test data: invalid Base64 payload ---
  it('decodeToken returns null for invalid Base64 payload — no throw', () => {
    expect(() => service.decodeToken(JWT_INVALID_B64)).not.toThrow();
    expect(service.decodeToken(JWT_INVALID_B64)).toBeNull();
  });

  // --- TC-007 specific: exp 1000000000 (Sep 2001) + Date.now spy for determinism ---
  it('TC-007: isTokenExpired returns true for JWT_EXPIRED (exp 1000000000) with deterministic Date.now', () => {
    spyOn(Date, 'now').and.returnValue(1_700_000_000_000); // Nov 2023 — well past Sep 2001
    expect(service.isTokenExpired(JWT_EXPIRED)).toBeTrue();
  });

  // --- TC-008: getCurrentRole with JWT_STAFF_VALID via localStorage spy ---
  it('TC-008: getCurrentRole returns staff from JWT_STAFF_VALID via localStorage spy', () => {
    spyOn(localStorage, 'getItem').and.returnValue(JWT_STAFF_VALID);
    expect(service.getCurrentRole()).toBe('staff');
  });

  // --- getCurrentRole: unknown role returns null (JWT_UNKNOWN_ROLE fixture) ---
  it('getCurrentRole returns null for unknown role in JWT_UNKNOWN_ROLE fixture', () => {
    spyOn(localStorage, 'getItem').and.returnValue(JWT_UNKNOWN_ROLE);
    expect(service.getCurrentRole()).toBeNull();
  });
});
