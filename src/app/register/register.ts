import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EMPTY, catchError, finalize, switchMap } from 'rxjs';
import { AccountService } from '../account.service';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class Register {
  private readonly accounts = inject(AccountService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly registered = signal(false);
  readonly selectedPhoto = signal<File | null>(null);
  readonly bookingIntent = this.readBookingIntent();

  readonly form = new FormGroup({
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(150)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[+()\d\s-]{7,30}$/)] }),
    address: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(5), Validators.maxLength(500)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    aadhaarNumber: new FormControl('', { nonNullable: true, validators: [Validators.pattern(/^\d{12}$/)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12), Validators.maxLength(200)] }),
  });

  selectPhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedPhoto.set(input.files?.[0] ?? null);
  }

  submit(): void {
    if (this.form.invalid || !this.selectedPhoto()) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();
    const payload = new FormData();
    payload.append('fullName', values.fullName.trim());
    payload.append('mobileNumber', values.mobileNumber.trim());
    payload.append('address', values.address.trim());
    payload.append('email', values.email.trim());
    payload.append('aadhaarNumber', values.aadhaarNumber);
    payload.append('password', values.password);
    payload.append('profilePhoto', this.selectedPhoto()!);

    this.errorMessage.set('');
    this.submitting.set(true);
    this.accounts.register(payload).pipe(
      switchMap(() => this.auth.login({ email: values.email.trim(), password: values.password }).pipe(
        catchError(() => {
          this.registered.set(true);
          this.errorMessage.set('Your account was created, but automatic sign-in failed. Sign in to continue.');
          return EMPTY;
        }),
      )),
      finalize(() => this.submitting.set(false)),
    ).subscribe({
      next: () => void this.router.navigate(['/home'], { queryParams: this.bookingIntent }),
      error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
    });
  }

  private describeError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) return 'The registration service could not be reached.';
      if (error.status === 409) return 'An account with this email already exists.';
      if (error.status === 400) return 'Check the entered details and choose a valid image up to 8 MB.';
    }

    return 'Registration could not be completed. Please try again.';
  }

  private readBookingIntent(): Record<string, string> {
    const params = this.route.snapshot.queryParamMap;
    const roomId = params.get('roomId');
    const checkInDate = params.get('checkInDate');
    const checkOutDate = params.get('checkOutDate');
    return roomId && checkInDate && checkOutDate
      ? { roomId, checkInDate, checkOutDate }
      : {};
  }
}