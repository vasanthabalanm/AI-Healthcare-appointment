import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

const TOKEN_KEY = 'access_token';

export interface RegisterPayload {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

export interface RegisterResponse {
  message: string;
}

export interface LoginPayload {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
}

export type UserRole = 'patient' | 'staff' | 'admin';

interface JwtPayload {
  sub: string;
  role: UserRole;
  exp: number;
  [key: string]: unknown;
}

@Injectable({ providedIn: 'root' })
export class AuthService {

  constructor(private http: HttpClient) {}

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
  }

  clearToken(): void {
    localStorage.removeItem(TOKEN_KEY);
  }

  /**
   * Decodes the JWT payload. Returns null on any malformed input — never throws.
   */
  decodeToken(token: string): JwtPayload | null {
    try {
      const parts = token.split('.');
      if (parts.length !== 3) { return null; }
      // Normalise Base64URL → Base64 (RFC 7519 §3: `-` → `+`, `_` → `/`)
      const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(b64)) as JwtPayload;
      if (typeof payload !== 'object' || payload === null) { return null; }
      return payload;
    } catch {
      return null;
    }
  }

  isTokenExpired(token: string): boolean {
    const payload = this.decodeToken(token);
    if (!payload || typeof payload['exp'] !== 'number') { return true; }
    return Date.now() >= payload['exp'] * 1000;
  }

  getCurrentRole(): UserRole | null {
    const token = this.getToken();
    if (!token) { return null; }
    const payload = this.decodeToken(token);
    const role = payload?.['role'];
    if (role === 'patient' || role === 'staff' || role === 'admin') {
      return role;
    }
    return null;
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) { return false; }
    return !this.isTokenExpired(token);
  }

  getCurrentUserId(): string | null {
    const token = this.getToken();
    if (!token) { return null; }
    const payload = this.decodeToken(token);
    return payload?.['sub'] ?? null;
  }

  register(payload: RegisterPayload): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${environment.apiBaseUrl}/auth/register`, payload);
  }

  login(payload: LoginPayload): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${environment.apiBaseUrl}/auth/login`, payload).pipe(
      tap(res => this.setToken(res.accessToken))
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${environment.apiBaseUrl}/auth/logout`, {}).pipe(
      tap(() => this.clearToken())
    );
  }

  extendSession(): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${environment.apiBaseUrl}/auth/extend-session`, {}).pipe(
      tap(res => this.setToken(res.accessToken))
    );
  }

  forgotPassword(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${environment.apiBaseUrl}/auth/forgot-password`, { email });
  }

  resetPassword(email: string, token: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${environment.apiBaseUrl}/auth/reset-password`, {
      email,
      token,
      newPassword
    });
  }

  setupCredentials(token: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${environment.apiBaseUrl}/auth/setup-credentials`, {
      token,
      newPassword
    });
  }
}
