import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './login.css',
  templateUrl: './login.html',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.errorMessage.set('');
    this.submitting.set(true);
    this.auth.login(this.form.getRawValue()).pipe(
      finalize(() => this.submitting.set(false)),
    ).subscribe({
      next: () => void this.router.navigate(['/home'], { queryParams: this.bookingIntent() }),
      error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
    });
  }

  private describeError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) return 'That email and password do not match.';
      if (error.status === 0) return 'The sign-in service could not be reached.';
    }

    return 'Sign-in could not be completed. Please try again.';
  }

  private bookingIntent(): Record<string, string> {
    const params = this.route.snapshot.queryParamMap;
    const roomId = params.get('roomId');
    const checkInDate = params.get('checkInDate');
    const checkOutDate = params.get('checkOutDate');
    return roomId && checkInDate && checkOutDate
      ? { roomId, checkInDate, checkOutDate }
      : {};
  }
}