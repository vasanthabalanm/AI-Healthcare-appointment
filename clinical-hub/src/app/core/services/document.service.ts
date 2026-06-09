import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PatientDocument {
  id: number;
  fileName: string;
  fileType: string;
  uploadedAt: string;
  ocrStatus: 'Pending' | 'Processing' | 'Complete' | 'Failed';
  ocrConfidence: number | null;   // 0-100, null if not processed
  fileSizeBytes: number;
}

export interface UploadDocumentResponse {
  documentId: number;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private api = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getDocuments(): Observable<PatientDocument[]> {
    return this.http.get<PatientDocument[]>(`${this.api}/patients/me/documents`);
  }

  uploadDocument(file: File): Observable<UploadDocumentResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<UploadDocumentResponse>(`${this.api}/patients/me/documents`, form);
  }

  previewDocument(id: number): Observable<Blob> {
    return this.http.get(`${this.api}/patients/me/documents/${id}/file`, { responseType: 'blob' });
  }

  deleteDocument(id: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/patients/me/documents/${id}`);
  }
}
