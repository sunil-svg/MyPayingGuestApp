import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface RoomPhoto {
  id: number;
  url: string;
  contentType: string;
}

export interface Room {
  id: number;
  roomNumber: string;
  name: string;
  floorNumber: number;
  capacity: number;
  monthlyRent: number;
  description: string;
  isAvailable: boolean;
  amenities: string[];
  photos: RoomPhoto[];
}

export interface RoomRequest {
  roomNumber: string;
  name: string;
  floorNumber: number;
  capacity: number;
  monthlyRent: number;
  description: string;
  isAvailable: boolean;
  amenities: string[];
}

export interface OccupiedRoom {
  roomId: number;
  roomNumber: string;
  name: string;
  floorNumber: number;
  capacity: number;
  monthlyRent: number;
  guestFullName: string;
  guestEmail: string;
  guestMobileNumber: string | null;
  checkInDate: string;
  checkOutDate: string;
}

export interface AdminRoomOverview {
  asOfDate: string;
  occupiedRoomsCount: number;
  availableRoomsCount: number;
  occupiedRooms: OccupiedRoom[];
  availableRooms: Room[];
}

@Injectable({ providedIn: 'root' })
export class RoomService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/rooms';

  getRooms(): Observable<Room[]> {
    return this.http.get<Room[]>(this.endpoint);
  }

  getAdminOverview(): Observable<AdminRoomOverview> {
    return this.http.get<AdminRoomOverview>('/api/admin/rooms/overview');
  }

  createRoom(request: RoomRequest): Observable<Room> {
    return this.http.post<Room>(this.endpoint, request);
  }

  updateRoom(id: number, request: RoomRequest): Observable<Room> {
    return this.http.put<Room>(`${this.endpoint}/${id}`, request);
  }

  deleteRoom(id: number): Observable<void> {
    return this.http.delete<void>(`${this.endpoint}/${id}`);
  }

  uploadPhoto(roomId: number, file: File): Observable<RoomPhoto> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<RoomPhoto>(`${this.endpoint}/${roomId}/photos`, formData);
  }

  deletePhoto(roomId: number, photoId: number): Observable<void> {
    return this.http.delete<void>(`${this.endpoint}/${roomId}/photos/${photoId}`);
  }

  getPhoto(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' });
  }
}