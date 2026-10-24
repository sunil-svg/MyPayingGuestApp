import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface RegistrationResponse {
  email: string;
  fullName: string;
  role: string;
}

export interface Booking {
  id: number;
  roomId: number;
  roomNumber: string;
  checkInDate: string;
  checkOutDate: string;
  monthlyRentAtBooking: number;
  status: string;
  createdAt: string;
}

export interface RoomAvailability {
  isAvailable: boolean;
}

@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);

  register(form: FormData): Observable<RegistrationResponse> {
    return this.http.post<RegistrationResponse>('/api/auth/register', form);
  }

  checkAvailability(roomId: number, checkInDate: string, checkOutDate: string): Observable<RoomAvailability> {
    const params = new HttpParams().set('checkInDate', checkInDate).set('checkOutDate', checkOutDate);
    return this.http.get<RoomAvailability>(`/api/rooms/${roomId}/availability`, { params });
  }

  getMyBookings(): Observable<Booking[]> {
    return this.http.get<Booking[]>('/api/bookings');
  }

  createBooking(roomId: number, checkInDate: string, checkOutDate: string): Observable<Booking> {
    return this.http.post<Booking>(`/api/rooms/${roomId}/bookings`, { checkInDate, checkOutDate });
  }

  cancelBooking(bookingId: number): Observable<void> {
    return this.http.delete<void>(`/api/bookings/${bookingId}`);
  }
}