import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let auth: AuthService;
  let http: HttpClient;
  let requests: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: { navigateByUrl: () => Promise.resolve(true) } },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    requests.verify();
    sessionStorage.clear();
  });

  it('attaches the bearer token to API requests', () => {
    auth.login({ email: 'user@example.com', password: 'secret' }).subscribe();
    requests.expectOne('/api/auth/login').flush({ accessToken: 'signed-token', role: 'user' });

    http.get('/api/profile').subscribe();
    const request = requests.expectOne('/api/profile');

    expect(request.request.headers.get('Authorization')).toBe('Bearer signed-token');
    request.flush({});
  });

  it('clears an expired session after an API 401 response', () => {
    auth.login({ email: 'user@example.com', password: 'secret' }).subscribe();
    requests.expectOne('/api/auth/login').flush({ accessToken: 'signed-token', role: 'user' });

    http.get('/api/profile').subscribe({ error: () => undefined });
    requests.expectOne('/api/profile').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentSession()).toBeNull();
  });
});