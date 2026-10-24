import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AccountService } from './account.service';

describe('AccountService', () => {
  let service: AccountService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AccountService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts multipart registration details', () => {
    const payload = new FormData();
    payload.append('email', 'new-user@example.com');
    service.register(payload).subscribe();

    const request = http.expectOne('/api/auth/register');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe(payload);
    request.flush({ email: 'new-user@example.com', fullName: 'New User', role: 'user' });
  });

  it('checks room availability for a selected date range', () => {
    service.checkAvailability(3, '2026-11-01', '2026-11-05').subscribe();

    const request = http.expectOne('/api/rooms/3/availability?checkInDate=2026-11-01&checkOutDate=2026-11-05');
    expect(request.request.method).toBe('GET');
    request.flush({ isAvailable: true });
  });

  it('creates a booking for the selected room and dates', () => {
    service.createBooking(3, '2026-11-01', '2026-11-05').subscribe();

    const request = http.expectOne('/api/rooms/3/bookings');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ checkInDate: '2026-11-01', checkOutDate: '2026-11-05' });
    request.flush({ id: 11, roomId: 3 });
  });
});