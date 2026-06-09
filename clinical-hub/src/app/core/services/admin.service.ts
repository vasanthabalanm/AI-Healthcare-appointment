import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AdminUser {
  id: number;
  email: string;
  firstName: string;
  lastName: string;
  role: 'admin' | 'staff';
  isActive: boolean;
  lastLoginAt: string | null;
}

export interface AdminUsersPage {
  items: AdminUser[];
  total: number;
  page: number;
  pageSize: number;
}

export interface CreateUserPayload {
  email: string;
  firstName: string;
  lastName: string;
  role: 'admin' | 'staff';
}

export interface UpdateUserPayload {
  firstName?: string;
  lastName?: string;
  isActive?: boolean;
}

export interface CreateUserResponse {
  message: string;
  userId: number;
}

@Injectable({ providedIn: 'root' })
export class AdminService {

  private base = `${environment.apiBaseUrl}/admin/users`;

  constructor(private http: HttpClient) {}

  getUsers(search = '', role = '', active = ''): Observable<AdminUser[]> {
    let url = this.base;
    const params: string[] = [];
    if (search) params.push(`search=${encodeURIComponent(search)}`);
    if (role)   params.push(`role=${encodeURIComponent(role)}`);
    if (active) params.push(`active=${encodeURIComponent(active)}`);
    if (params.length) url += '?' + params.join('&');
    return this.http.get<AdminUsersPage>(url).pipe(map(r => r.items));
  }

  createUser(payload: CreateUserPayload): Observable<CreateUserResponse> {
    return this.http.post<CreateUserResponse>(this.base, payload);
  }

  updateUser(id: number, payload: UpdateUserPayload): Observable<void> {
    return this.http.patch<void>(`${this.base}/${id}`, payload);
  }
}
