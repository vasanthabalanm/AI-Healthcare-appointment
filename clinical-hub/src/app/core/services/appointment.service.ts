import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── Models ────────────────────────────────────────────────────────────────────

export interface Slot {
  id: number;
  slotTime: string;        // ISO-8601
  durationMinutes: number;
  isAvailable: boolean | null;  // null when fetched without allSlots=true
}

export interface BookAppointmentPayload {
  slotId: number;
}

export interface BookAppointmentResponse {
  appointmentId: number;
}

export type AppointmentStatus = 'Scheduled' | 'Arrived' | 'Completed' | 'Cancelled' | 'NoShow';

export interface Appointment {
  id: number;
  slotId: number;
  slotTime: string;        // ISO-8601 — joined from Slot
  status: AppointmentStatus;
  bookedAt: string;
  noShowRiskScore: number;
  isHighRisk: boolean;
}

export interface ReschedulePayload {
  newSlotId: number;
}

export interface RescheduleResponse {
  appointmentId: number;
  newSlotId: number;
}

export interface JoinWaitlistPayload {
  preferredSlotId?: number;
  preferredSlotDate: string;   // yyyy-MM-dd
}

export interface JoinWaitlistResponse {
  waitlistEntryId: number;
  message: string;
}

export interface ScheduleItem {
  appointmentId: number;
  patientName: string;
  slotTime: string;
  durationMinutes: number;
  status: string;
  noShowRiskScore: number;
  isHighRisk: boolean;
}

// Staff daily schedule (GET /schedule/today)
export interface ScheduleEntry {
  appointmentId: number;
  patientId: number;
  patientName: string;
  slotTime: string;
  status: string;
  intakeStatus: string;   // 'Submitted' | 'Pending' | 'NA'
  riskFlag: boolean;
}

export interface SchedulePageResponse {
  page: number;
  pageSize: number;
  totalCount: number;
  pageCount: number;
  data: ScheduleEntry[];
}

// Queue entry (GET /staff/queue)
export interface QueueEntry {
  id: number;
  position: number;
  patientId: number;
  patientName: string;
  arrivalType: 'WalkIn' | 'Appointment';
  arrivedAt: string;
  status: 'Waiting' | 'InProgress' | 'Completed' | 'Removed';
  chiefComplaint: string;
  waitMinutes: number;
}

// ── Service ───────────────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class AppointmentService {

  private api = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  // US_019 — GET /slots?date=yyyy-MM-dd
  getSlots(date: string): Observable<Slot[]> {
    const params = new HttpParams().set('date', date);
    return this.http.get<Slot[]>(`${this.api}/slots`, { params });
  }

  // Calendar prefetch — GET /slots?date=yyyy-MM-dd&allSlots=true
  // Returns all slots (available + unavailable) so the calendar can show fully-booked days.
  getAllSlots(date: string): Observable<Slot[]> {
    const params = new HttpParams().set('date', date).set('allSlots', 'true');
    return this.http.get<Slot[]>(`${this.api}/slots`, { params });
  }

  // US_019 — POST /appointments
  bookAppointment(payload: BookAppointmentPayload): Observable<BookAppointmentResponse> {
    return this.http.post<BookAppointmentResponse>(`${this.api}/appointments`, payload);
  }

  // US_023 — GET /appointments (patient's own appointments — assumed endpoint)
  getMyAppointments(): Observable<Appointment[]> {
    return this.http.get<Appointment[]>(`${this.api}/appointments/mine`);
  }

  // US_023 — DELETE /appointments/{id}
  cancelAppointment(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.api}/appointments/${id}`);
  }

  // US_023 — PATCH /appointments/{id}/reschedule
  rescheduleAppointment(id: number, payload: ReschedulePayload): Observable<RescheduleResponse> {
    return this.http.patch<RescheduleResponse>(`${this.api}/appointments/${id}/reschedule`, payload);
  }

  // US_020 — POST /waitlist
  joinWaitlist(payload: JoinWaitlistPayload): Observable<JoinWaitlistResponse> {
    return this.http.post<JoinWaitlistResponse>(`${this.api}/waitlist`, payload);
  }

  // US_021 — POST /waitlist/{id}/accept
  acceptSwapOffer(waitlistId: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/waitlist/${waitlistId}/accept`, {});
  }

  // US_021 — POST /waitlist/{id}/decline
  declineSwapOffer(waitlistId: number): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.api}/waitlist/${waitlistId}/decline`, {});
  }

  // US_022 — GET /appointments/schedule?date=yyyy-MM-dd  (staff/admin)
  getSchedule(date: string): Observable<ScheduleItem[]> {
    const params = new HttpParams().set('date', date);
    return this.http.get<ScheduleItem[]>(`${this.api}/appointments/schedule`, { params });
  }

  // Staff — GET /schedule/today[?date=]
  getStaffSchedule(date?: string): Observable<SchedulePageResponse> {
    const params = date ? new HttpParams().set('date', date) : new HttpParams();
    return this.http.get<SchedulePageResponse>(`${this.api}/schedule/today`, { params });
  }

  // Staff — PATCH /appointments/{id}/checkin
  checkIn(appointmentId: number): Observable<void> {
    return this.http.patch<void>(`${this.api}/appointments/${appointmentId}/checkin`, {});
  }

  // Staff — GET /staff/queue
  getQueue(): Observable<QueueEntry[]> {
    return this.http.get<QueueEntry[]>(`${this.api}/staff/queue`);
  }

  // Staff — PATCH /staff/queue/reorder
  reorderQueue(entries: { id: number; position: number }[]): Observable<void> {
    return this.http.patch<void>(`${this.api}/staff/queue/reorder`, { entries });
  }

  // Staff — DELETE /staff/queue/{entryId}
  removeFromQueue(entryId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/staff/queue/${entryId}`);
  }

  // Staff — GET /staff/patients/search?q=
  searchPatients(q: string): Observable<any[]> {
    const params = new HttpParams().set('q', q);
    return this.http.get<any[]>(`${this.api}/staff/patients/search`, { params });
  }

  // Staff — POST /staff/patients/walkin
  registerWalkIn(payload: any): Observable<any> {
    return this.http.post<any>(`${this.api}/staff/patients/walkin`, payload);
  }

  // Staff — GET /patients/{id}/view360
  get360View(patientId: number): Observable<any> {
    return this.http.get<any>(`${this.api}/patients/${patientId}/view360`);
  }

  // Staff — PATCH /patients/{id}/verify
  verifyPatient(patientId: number): Observable<any> {
    return this.http.patch<any>(`${this.api}/patients/${patientId}/verify`, {});
  }

  // Staff — PATCH /appointments/{id}/status
  updateAppointmentStatus(appointmentId: number, status: string): Observable<void> {
    return this.http.patch<void>(`${this.api}/appointments/${appointmentId}/status`, { status });
  }

  // Staff — PATCH /staff/queue/{entryId}/status
  updateQueueStatus(entryId: number, status: string): Observable<void> {
    return this.http.patch<void>(`${this.api}/staff/queue/${entryId}/status`, { status });
  }
}
