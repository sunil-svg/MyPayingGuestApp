import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin, of, switchMap } from 'rxjs';
import { AccountService, Booking } from '../account.service';
import { AuthService } from '../auth.service';
import { AdminRoomOverview, Room, RoomRequest, RoomService } from '../room.service';

function dateInputValue(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

@Component({
  selector: 'app-home',
  imports: [CurrencyPipe, ReactiveFormsModule, RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.css',
})
export class Home {
  readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly accountApi = inject(AccountService);
  private readonly roomsApi = inject(RoomService);
  private readonly destroyRef = inject(DestroyRef);
  readonly rooms = signal<Room[]>([]);
  readonly adminOverview = signal<AdminRoomOverview | null>(null);
  readonly adminOverviewLoading = signal(false);
  readonly adminRoomFilter = signal<'all' | 'occupied' | 'available'>('all');
  readonly visibleRooms = computed(() => {
    if (!this.isAdmin() || this.adminRoomFilter() === 'all') return this.rooms();
    if (this.adminRoomFilter() === 'available') return this.adminOverview()?.availableRooms ?? [];
    return [];
  });
  readonly photoUrls = signal<Record<number, string>>({});
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editorOpen = signal(false);
  readonly editingRoomId = signal<number | null>(null);
  readonly isAdmin = computed(() => this.auth.currentSession()?.role === 'admin');
  readonly isUser = computed(() => this.auth.currentSession()?.role === 'user');
  readonly errorMessage = signal('');
  readonly successMessage = signal('');
  readonly bookingError = signal('');
  readonly bookingMessage = signal('');
  readonly bookings = signal<Booking[]>([]);
  readonly availability = signal<Record<number, boolean | null>>({});
  readonly bookingInProgress = signal<number | null>(null);
  readonly today = dateInputValue(new Date());
  readonly tomorrow = dateInputValue(new Date(new Date().setDate(new Date().getDate() + 1)));
  readonly selectedPhotos = signal<File[]>([]);
  private readonly objectUrls = new Set<string>();
  private availabilityGeneration = 0;
  readonly bookingDates = new FormGroup({
    checkInDate: new FormControl(this.today, { nonNullable: true, validators: [Validators.required] }),
    checkOutDate: new FormControl(this.tomorrow, { nonNullable: true, validators: [Validators.required] }),
  });

  readonly form = new FormGroup({
    roomNumber: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(30)],
    }),
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(100)],
    }),
    floorNumber: new FormControl(0, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(0), Validators.max(200)],
    }),
    capacity: new FormControl(1, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(20)],
    }),
    monthlyRent: new FormControl(0, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(0.01), Validators.max(1000000)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(1200)],
    }),
    amenities: new FormControl('', { nonNullable: true }),
    isAvailable: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    this.loadRooms();
    if (this.isUser()) this.loadBookings();
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const roomId = Number(params.get('roomId'));
      const checkInDate = params.get('checkInDate') ?? '';
      const checkOutDate = params.get('checkOutDate') ?? '';
      if (Number.isInteger(roomId) && roomId > 0 && checkInDate && checkOutDate) {
        this.bookingDates.setValue({ checkInDate, checkOutDate });
        this.checkRoomsAvailability(this.rooms());
      }
    });
    this.destroyRef.onDestroy(() => this.objectUrls.forEach((url) => URL.revokeObjectURL(url)));
  }

  clearAvailability(): void {
    this.availability.set({});
    this.bookingError.set('');
    this.bookingMessage.set('');
  }

  onBookingDatesChanged(): void {
    this.clearAvailability();
    this.checkRoomsAvailability(this.rooms());
  }

  bookingIntentQueryParams(roomId: number): Record<string, string | number> {
    const { checkInDate, checkOutDate } = this.bookingDates.getRawValue();
    return { roomId, checkInDate, checkOutDate };
  }

  private checkAvailability(room: Room, generation: number): void {
    if (this.bookingDates.invalid) {
      this.bookingDates.markAllAsTouched();
      this.bookingError.set('Choose check-in and check-out dates first.');
      return;
    }

    const { checkInDate, checkOutDate } = this.bookingDates.getRawValue();
    if (checkInDate < this.today || checkOutDate <= checkInDate) {
      this.bookingError.set('Choose valid dates. Check-out must be after check-in.');
      this.availability.update((values) => ({ ...values, [room.id]: false }));
      return;
    }

    this.bookingError.set('');
    this.accountApi.checkAvailability(room.id, checkInDate, checkOutDate).subscribe({
      next: (result) => {
        if (generation === this.availabilityGeneration) {
          this.availability.update((values) => ({ ...values, [room.id]: result.isAvailable }));
        }
      },
      error: (error: unknown) => {
        if (generation === this.availabilityGeneration) {
          this.availability.update((values) => ({ ...values, [room.id]: false }));
          this.bookingError.set(this.describeBookingError(error));
        }
      },
    });
  }

  bookRoom(room: Room): void {
    if (this.availability()[room.id] !== true) {
      this.bookingError.set('Check availability for your dates before booking.');
      return;
    }

    const { checkInDate, checkOutDate } = this.bookingDates.getRawValue();
    this.bookingInProgress.set(room.id);
    this.bookingError.set('');
    this.bookingMessage.set('');
    this.accountApi.createBooking(room.id, checkInDate, checkOutDate).pipe(
      finalize(() => this.bookingInProgress.set(null)),
    ).subscribe({
      next: () => {
        this.bookingMessage.set(`Booking confirmed: room ${room.roomNumber}, ${checkInDate} to ${checkOutDate}.`);
        this.availability.update((values) => ({ ...values, [room.id]: false }));
        this.loadBookings();
      },
      error: (error: unknown) => {
        this.availability.update((values) => ({ ...values, [room.id]: false }));
        this.bookingError.set(this.describeBookingError(error));
      },
    });
  }

  loadBookings(): void {
    this.accountApi.getMyBookings().subscribe({
      next: (bookings) => this.bookings.set(bookings),
      error: (error: unknown) => this.bookingError.set(this.describeBookingError(error)),
    });
  }

  cancelBooking(booking: Booking): void {
    this.accountApi.cancelBooking(booking.id).subscribe({
      next: () => {
        this.bookingMessage.set(`Booking for room ${booking.roomNumber} cancelled.`);
        this.loadBookings();
      },
      error: (error: unknown) => this.bookingError.set(this.describeBookingError(error)),
    });
  }

  loadRooms(): void {
    if (this.isAdmin()) this.loadAdminOverview();
    this.loading.set(true);
    this.errorMessage.set('');
    this.roomsApi.getRooms().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: (rooms) => {
        this.rooms.set(rooms);
        this.checkRoomsAvailability(rooms);
        this.clearPhotoUrls();
        for (const room of rooms) {
          for (const photo of room.photos) {
            this.roomsApi.getPhoto(photo.url).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
              next: (blob) => {
                const url = URL.createObjectURL(blob);
                this.objectUrls.add(url);
                this.photoUrls.update((values) => ({ ...values, [photo.id]: url }));
              },
            });
          }
        }
      },
      error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
    });
  }

  setAdminRoomFilter(filter: 'all' | 'occupied' | 'available'): void {
    this.adminRoomFilter.set(filter);
  }

  private loadAdminOverview(): void {
    this.adminOverviewLoading.set(true);
    this.roomsApi.getAdminOverview().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.adminOverviewLoading.set(false)),
    ).subscribe({
      next: (overview) => this.adminOverview.set(overview),
      error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
    });
  }

  openCreate(): void {
    this.editingRoomId.set(null);
    this.selectedPhotos.set([]);
    this.errorMessage.set('');
    this.form.reset({ roomNumber: '', name: '', floorNumber: 0, capacity: 1, monthlyRent: 0, description: '', amenities: '', isAvailable: true });
    this.editorOpen.set(true);
  }

  openEdit(room: Room): void {
    this.editingRoomId.set(room.id);
    this.selectedPhotos.set([]);
    this.errorMessage.set('');
    this.form.reset({
      roomNumber: room.roomNumber,
      name: room.name || room.roomNumber,
      floorNumber: room.floorNumber,
      capacity: room.capacity || 1,
      monthlyRent: room.monthlyRent,
      description: room.description,
      amenities: room.amenities.join(', '),
      isAvailable: room.isAvailable,
    });
    this.editorOpen.set(true);
  }

  closeEditor(): void {
    if (!this.saving()) {
      this.editorOpen.set(false);
    }
  }

  selectPhotos(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedPhotos.set(Array.from(input.files ?? []));
  }

  saveRoom(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();
    const request: RoomRequest = {
      roomNumber: values.roomNumber.trim(),
      name: values.name.trim(),
      floorNumber: Number(values.floorNumber),
      capacity: Number(values.capacity),
      monthlyRent: Number(values.monthlyRent),
      description: values.description.trim(),
      isAvailable: values.isAvailable,
      amenities: values.amenities.split(',').map((value) => value.trim()).filter(Boolean),
    };
    const roomId = this.editingRoomId();
    const saveRequest = roomId === null
      ? this.roomsApi.createRoom(request)
      : this.roomsApi.updateRoom(roomId, request);
    const uploads = this.selectedPhotos();

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    saveRequest.pipe(
      switchMap((room) => uploads.length
        ? forkJoin(uploads.map((file) => this.roomsApi.uploadPhoto(room.id, file))).pipe(switchMap(() => of(room)))
        : of(room)),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(false)),
    ).subscribe({
      next: () => {
        this.editorOpen.set(false);
        this.successMessage.set(roomId === null ? 'Room added.' : 'Room updated.');
        this.loadRooms();
      },
      error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
    });
  }

  deleteRoom(room: Room): void {
    if (!confirm(`Delete room ${room.roomNumber} and its photos?`)) {
      return;
    }

    this.roomsApi.deleteRoom(room.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.successMessage.set(`Room ${room.roomNumber} deleted.`);
        this.loadRooms();
      },
      error: (error: unknown) => this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 409
          ? 'This room has booking history and cannot be deleted. Mark it unavailable instead.'
          : this.describeError(error),
      ),
    });
  }

  deletePhoto(room: Room, photoId: number): void {
    this.roomsApi.deletePhoto(room.id, photoId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.loadRooms(),
      error: (error: unknown) => this.errorMessage.set(this.describeError(error)),
    });
  }

  private checkRoomsAvailability(rooms: Room[]): void {
    const generation = ++this.availabilityGeneration;
    this.availability.set(Object.fromEntries(rooms.map((room) => [room.id, null])));
    for (const room of rooms) this.checkAvailability(room, generation);
  }

  signOut(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/login');
  }

  private clearPhotoUrls(): void {
    this.objectUrls.forEach((url) => URL.revokeObjectURL(url));
    this.objectUrls.clear();
    this.photoUrls.set({});
  }

  private describeError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) return 'The room service could not be reached.';
      if (error.status === 400) return 'Some room details or selected photos are invalid.';
      if (error.status === 403) return 'Admin access is required for that action.';
      if (error.status === 404) return 'This room is no longer available. Refresh the room list and try again.';
      if (error.status === 409) return 'That room number is already in use.';
      if (error.status >= 500) return 'The server could not save this room. Please try again.';
    }

    return 'The room request could not be completed. Check the room data and try again.';
  }

  private describeBookingError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) return 'The booking service could not be reached.';
      if (error.status === 400) return 'Choose a valid check-in and check-out date.';
      if (error.status === 403) return 'Only user accounts can book rooms.';
      if (error.status === 404) return 'This room or booking could not be found.';
      if (error.status === 409) return 'Those dates are no longer available. Check another date range.';
    }

    return 'The booking request could not be completed. Please try again.';
  }

}