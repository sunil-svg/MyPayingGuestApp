import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';

export type UserRole = 'admin' | 'user';

export interface AuthSession {
  accessToken: string;
  role: UserRole;
}

export interface LoginCredentials {
  email: string;
  password: string;
}

interface LoginResponse {
  accessToken: string;
  role: string;
}

const SESSION_KEY = 'my-angular-app.auth-session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly sessionState = signal<AuthSession | null>(this.readSession());
  readonly currentSession = this.sessionState.asReadonly();

  constructor(private readonly http: HttpClient) {}

  login(credentials: LoginCredentials): Observable<AuthSession> {
    return this.http.post<LoginResponse>('/api/auth/login', credentials).pipe(
      map((response) => {
        if (!response.accessToken || !this.isUserRole(response.role)) {
          throw new Error('The authentication response is invalid.');
        }

        return { accessToken: response.accessToken, role: response.role };
      }),
      tap((session) => {
        sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
        this.sessionState.set(session);
      }),
    );
  }

  isAuthenticated(): boolean {
    return Boolean(this.sessionState()?.accessToken);
  }

  logout(): void {
    sessionStorage.removeItem(SESSION_KEY);
    this.sessionState.set(null);
  }

  private readSession(): AuthSession | null {
    try {
      const stored = sessionStorage.getItem(SESSION_KEY);
      if (!stored) return null;

      const session: unknown = JSON.parse(stored);
      if (
        typeof session === 'object' &&
        session !== null &&
        'accessToken' in session &&
        typeof session.accessToken === 'string' &&
        session.accessToken.length > 0 &&
        'role' in session &&
        this.isUserRole(session.role)
      ) {
        return { accessToken: session.accessToken, role: session.role };
      }
    } catch {
      sessionStorage.removeItem(SESSION_KEY);
    }

    return null;
  }

  private isUserRole(role: unknown): role is UserRole {
    return role === 'admin' || role === 'user';
  }
}