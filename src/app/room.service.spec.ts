import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RoomService } from './room.service';

describe('RoomService', () => {
  let service: RoomService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RoomService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads room records from the protected room endpoint', () => {
    let received: unknown;
    service.getRooms().subscribe((rooms) => received = rooms);

    const request = http.expectOne('/api/rooms');
    expect(request.request.method).toBe('GET');
    request.flush([]);

    expect(received).toEqual([]);
  });

  it('requests occupancy details from the admin overview endpoint', () => {
    service.getAdminOverview().subscribe();

    const request = http.expectOne('/api/admin/rooms/overview');
    expect(request.request.method).toBe('GET');
    request.flush({
      asOfDate: '2026-10-24',
      occupiedRoomsCount: 0,
      availableRoomsCount: 0,
      occupiedRooms: [],
      availableRooms: [],
    });
  });

  it('sends room details when creating a room', () => {
    const roomRequest = {
      roomNumber: '204',
      name: 'Garden Suite',
      floorNumber: 2,
      capacity: 2,
      monthlyRent: 1250,
      description: 'Balcony room',
      isAvailable: true,
      amenities: ['Wi-Fi'],
    };
    service.createRoom(roomRequest).subscribe();

    const request = http.expectOne('/api/rooms');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(roomRequest);
    request.flush({ id: 1, ...roomRequest, photos: [] });
  });

  it('sends selected photos as multipart form data', () => {
    const photo = new File(['image'], 'room.png', { type: 'image/png' });
    service.uploadPhoto(7, photo).subscribe();

    const request = http.expectOne('/api/rooms/7/photos');
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('file')).toBe(photo);
    request.flush({ id: 4, url: '/api/rooms/7/photos/4', contentType: 'image/png' });
  });
});