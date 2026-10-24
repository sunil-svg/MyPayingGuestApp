import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('posts credentials and stores a valid admin session', () => {
    let role: string | undefined;
    auth.login({ email: 'admin@example.com', password: 'secret' }).subscribe((session) => {
      role = session.role;
    });

    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'admin@example.com', password: 'secret' });
    request.flush({ accessToken: 'signed-token', role: 'admin' });

    expect(role).toBe('admin');
    expect(auth.currentSession()?.accessToken).toBe('signed-token');
    expect(auth.isAuthenticated()).toBe(true);
    expect(sessionStorage.length).toBe(1);
  });

  it('rejects roles outside the supported admin and user set', () => {
    let receivedError: unknown;
    auth.login({ email: 'person@example.com', password: 'secret' }).subscribe({
      error: (error: unknown) => receivedError = error,
    });

    http.expectOne('/api/auth/login').flush({ accessToken: 'signed-token', role: 'owner' });

    expect(receivedError).toBeInstanceOf(Error);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('clears the session when signing out', () => {
    auth.login({ email: 'user@example.com', password: 'secret' }).subscribe();
    http.expectOne('/api/auth/login').flush({ accessToken: 'signed-token', role: 'user' });

    auth.logout();

    expect(auth.currentSession()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.length).toBe(0);
  });
});