import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AuditLogEntry {
  id: number;
  timestamp: string;          // ISO-8601
  actorId: number;
  actorName: string;
  actorRole: string;
  action: string;
  detail: string;
  entityId: string;
}

export interface AuditLogPage {
  items: AuditLogEntry[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AuditFilters {
  from?: string;
  to?: string;
  actor?: string;
  action?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AuditService {
  private base = `${environment.apiBaseUrl}/admin/audit`;

  constructor(private http: HttpClient) {}

  getLog(filters: AuditFilters = {}): Observable<AuditLogPage> {
    let params = new HttpParams();
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.actor) params = params.set('actor', filters.actor);
    if (filters.action) params = params.set('action', filters.action);
    if (filters.page != null) params = params.set('page', String(filters.page));
    if (filters.pageSize != null) params = params.set('pageSize', String(filters.pageSize));
    return this.http.get<AuditLogPage>(this.base, { params });
  }

  exportCsv(filters: AuditFilters = {}): Observable<Blob> {
    let params = new HttpParams();
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.actor) params = params.set('actor', filters.actor);
    if (filters.action) params = params.set('action', filters.action);
    return this.http.get(`${this.base}/export`, { params, responseType: 'blob' });
  }
}
