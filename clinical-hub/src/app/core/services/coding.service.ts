import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type CodeType = 'ICD10' | 'CPT';
export type SuggestionStatus = 'Pending' | 'Accepted' | 'Modified' | 'Rejected';

export interface CodeSuggestion {
  id: number;
  codeType: CodeType;
  code: string;
  description: string;
  confidenceScore: number;   // 0-100
  status: SuggestionStatus;
}

export interface PatchSuggestionPayload {
  status: SuggestionStatus;
  verifiedById?: number;
  modifiedCode?: string;
  modifiedDescription?: string;
}

export interface AcceptAllResponse {
  acceptedCount: number;
}

@Injectable({ providedIn: 'root' })
export class CodingService {
  private api = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getSuggestions(patientId: number): Observable<CodeSuggestion[]> {
    return this.http.get<CodeSuggestion[]>(`${this.api}/patients/${patientId}/code-suggestions`);
  }

  patchSuggestion(id: number, payload: PatchSuggestionPayload): Observable<void> {
    return this.http.patch<void>(`${this.api}/code-suggestions/${id}`, payload);
  }

  acceptAll(patientId: number, verifiedById: number): Observable<AcceptAllResponse> {
    return this.http.post<AcceptAllResponse>(`${this.api}/patients/${patientId}/code-suggestions/accept-all`, { verifiedById });
  }

  completeCoding(patientId: number): Observable<void> {
    return this.http.post<void>(`${this.api}/patients/${patientId}/coding-complete`, {});
  }

  generateCodes(patientId: number, type: 'ICD10' | 'CPT'): Observable<void> {
    return this.http.post<void>(`${this.api}/patients/${patientId}/generate-codes?type=${type}`, {});
  }
}
