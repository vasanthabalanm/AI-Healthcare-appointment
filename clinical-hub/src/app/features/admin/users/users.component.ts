import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AdminService, AdminUser } from '../../../core/services/admin.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-admin-users',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <!-- Toast -->
    <div class="toast" *ngIf="toastMessage">{{ toastMessage }}</div>

    <!-- Page header -->
    <div class="page-header">
      <h1 class="page-title">User account management</h1>
      <button class="btn btn--primary" (click)="openCreateModal()">+ Create user</button>
    </div>

    <!-- Filter row -->
    <div class="filter-row">
      <input type="search" class="filter-input" placeholder="Search by name or email…"
        [value]="searchQuery" (input)="onSearch($event)">
      <select class="filter-select" (change)="onRoleFilter($event)">
        <option value="">All roles</option>
        <option value="admin">Admin</option>
        <option value="staff">Staff</option>
      </select>
      <select class="filter-select" (change)="onStatusFilter($event)">
        <option value="">All statuses</option>
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </select>
    </div>

    <!-- Loading / error -->
    <div class="state-msg" *ngIf="loading">Loading users&hellip;</div>
    <div class="state-msg state-msg--error" *ngIf="loadError">{{ loadError }}</div>

    <!-- User table -->
    <div class="table-wrap" *ngIf="!loading && !loadError">
      <div class="table-header">
        <span>Name / ID</span>
        <span>Role</span>
        <span>Email</span>
        <span>Status</span>
        <span>Last active</span>
        <span>Actions</span>
      </div>
      <div class="table-row" *ngFor="let user of filteredUsers"
        [class.table-row--inactive]="!user.isActive">
        <div class="user-cell">
          <span class="user-name">{{ user.firstName }} {{ user.lastName }}</span>
          <span class="user-id">#{{ user.id }}</span>
        </div>
        <div>
          <span class="badge" [class]="roleBadgeClass(user.role)">{{ user.role }}</span>
        </div>
        <div class="email-cell">{{ user.email }}</div>
        <div>
          <span class="badge" [class]="statusBadgeClass(user.isActive)">
            {{ user.isActive ? 'Active' : 'Inactive' }}
          </span>
        </div>
        <div class="last-active">{{ formatDate(user.lastLoginAt) }}</div>
        <div class="actions-cell">
          <button class="btn btn--secondary btn--sm" (click)="openEditModal(user)">Edit</button>
          <ng-container *ngIf="user.isActive">
            <span class="self-deactivate-note" *ngIf="isSelf(user); else deactivateBtn">Cannot self-deactivate</span>
            <ng-template #deactivateBtn>
              <button class="btn btn--deactivate btn--sm" (click)="openDeactivateModal(user)">Deactivate</button>
            </ng-template>
          </ng-container>
          <button class="btn btn--activate btn--sm" *ngIf="!user.isActive" (click)="reactivate(user)">Reactivate</button>
        </div>
      </div>
      <div class="state-msg" *ngIf="filteredUsers.length === 0">No users match the current filters.</div>
    </div>

    <!-- Create / Edit modal -->
    <div class="modal-backdrop" *ngIf="showUserModal" (click)="closeUserModal()">
      <div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-header">
          <h2 class="modal-title">{{ editTarget ? 'Edit user' : 'Create user' }}</h2>
          <button class="modal-close" (click)="closeUserModal()" aria-label="Close">&times;</button>
        </div>
        <div class="modal-error" *ngIf="modalError">{{ modalError }}</div>
        <form [formGroup]="userForm" (ngSubmit)="submitUserForm()" novalidate>
          <div class="row-2col">
            <div class="form-group">
              <label class="form-label" for="mFirstName">First name <span class="req">*</span></label>
              <input id="mFirstName" type="text" class="form-input"
                [class.form-input--error]="mInvalid('firstName')"
                formControlName="firstName">
              <span class="form-error" *ngIf="mInvalid('firstName')">Required.</span>
            </div>
            <div class="form-group">
              <label class="form-label" for="mLastName">Last name <span class="req">*</span></label>
              <input id="mLastName" type="text" class="form-input"
                [class.form-input--error]="mInvalid('lastName')"
                formControlName="lastName">
              <span class="form-error" *ngIf="mInvalid('lastName')">Required.</span>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label" for="mEmail">Email address <span class="req">*</span></label>
            <input id="mEmail" type="email" class="form-input"
              [class.form-input--error]="mInvalid('email')"
              formControlName="email" [attr.readonly]="editTarget ? true : null">
            <span class="form-error" *ngIf="mInvalid('email')">Valid email required.</span>
          </div>
          <div class="form-group" *ngIf="!editTarget">
            <label class="form-label" for="mRole">Role <span class="req">*</span></label>
            <select id="mRole" class="form-input" formControlName="role">
              <option value="staff">Staff</option>
              <option value="admin">Admin</option>
            </select>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn--secondary" (click)="closeUserModal()">Cancel</button>
            <button type="submit" class="btn btn--primary" [disabled]="modalLoading">
              {{ modalLoading ? 'Saving\u2026' : (editTarget ? 'Save changes' : 'Create user') }}
            </button>
          </div>
        </form>
      </div>
    </div>

    <!-- Deactivate confirmation modal -->
    <div class="modal-backdrop" *ngIf="showDeactivateModal" (click)="closeDeactivateModal()">
      <div class="modal modal--sm" (click)="$event.stopPropagation()">
        <div class="modal-header">
          <h2 class="modal-title">Deactivate user?</h2>
          <button class="modal-close" (click)="closeDeactivateModal()" aria-label="Close">&times;</button>
        </div>
        <p class="modal-body">
          <strong>{{ deactivateTarget?.firstName }} {{ deactivateTarget?.lastName }}</strong> will no longer be able to sign in.
          This action is recorded in the audit log.
        </p>
        <div class="modal-error" *ngIf="deactivateError">{{ deactivateError }}</div>
        <div class="modal-footer">
          <button type="button" class="btn btn--secondary" (click)="closeDeactivateModal()">Cancel</button>
          <button type="button" class="btn btn--deactivate" [disabled]="modalLoading" (click)="confirmDeactivate()">
            {{ modalLoading ? 'Deactivating\u2026' : 'Deactivate' }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* Page */
    .page-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--sp6); }
    .page-title { font-size: 24px; font-weight: 600; color: var(--ct1); margin: 0; }
    /* Filter row */
    .filter-row { display: flex; gap: var(--sp3); margin-bottom: var(--sp5); flex-wrap: wrap; }
    .filter-input { width: 260px; padding: 8px 12px; border: 1px solid var(--cb); border-radius: var(--r2); font-size: 13px; font-family: var(--ff, inherit); outline: none; }
    .filter-input:focus { border-color: var(--cp); box-shadow: 0 0 0 3px rgba(15,107,107,.35); }
    .filter-select { padding: 8px 12px; border: 1px solid var(--cb); border-radius: var(--r2); font-size: 13px; font-family: var(--ff, inherit); background: var(--cs0); outline: none; }
    .filter-select:focus { border-color: var(--cp); box-shadow: 0 0 0 3px rgba(15,107,107,.35); }
    /* Table */
    .table-wrap { border: 1px solid var(--cb); border-radius: var(--r3); overflow: hidden; }
    .table-header, .table-row {
      display: grid;
      grid-template-columns: 220px 120px 1fr 120px 130px 150px;
      align-items: center;
      gap: var(--sp4);
      padding: 10px 20px;
    }
    .table-row { padding: 14px 20px; }
    .table-header { background: var(--cs1); font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; color: var(--ct2); border-bottom: 2px solid var(--cb); }
    .table-row { border-bottom: 1px solid var(--cs2); font-size: 14px; transition: background .1s; }
    .table-row:last-child { border-bottom: none; }
    .table-row:hover { background: var(--cs1); }
    .table-row--inactive { opacity: 0.65; }
    .user-cell { display: flex; flex-direction: column; gap: 2px; }
    .user-name { font-weight: 500; color: var(--ct1); }
    .user-id { font-size: 12px; color: var(--ctd); }
    .email-cell { color: var(--ct2); font-size: 13px; word-break: break-all; }
    .last-active { font-size: 13px; color: var(--ct2); }
    .actions-cell { display: flex; align-items: center; gap: var(--sp2); flex-wrap: wrap; }
    .self-deactivate-note { font-size: 12px; color: var(--ctd); font-style: italic; }
    /* Badges */
    .badge { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 12px; font-weight: 500; }
    .badge--staff { background: var(--cps); color: var(--cp); }
    .badge--admin { background: #E8D5F5; color: #5A0A7A; }
    .badge--active { background: var(--cokb); color: var(--cok); }
    .badge--inactive { background: var(--cs2); color: var(--ct2); }
    /* Buttons */
    .btn {
      display: inline-flex; align-items: center; justify-content: center;
      height: 36px; padding: 0 var(--sp4); border: none; border-radius: var(--r2);
      font-size: 14px; font-weight: 500; cursor: pointer; transition: background .15s;
    }
    .btn--sm { height: 30px; font-size: 13px; padding: 0 var(--sp3); }
    .btn--primary { background: var(--cp); color: var(--cti); }
    .btn--primary:hover:not(:disabled) { background: var(--cph); }
    .btn--primary:disabled { opacity: .6; cursor: not-allowed; }
    .btn--secondary { background: var(--cs2); color: var(--ct1); border: 1px solid var(--cb); }
    .btn--secondary:hover { background: var(--cb); }
    .btn--deactivate { background: var(--cs0); color: var(--ce); border: 1px solid var(--ce); }
    .btn--deactivate:hover:not(:disabled) { background: var(--ceb); }
    .btn--deactivate:disabled { opacity: .6; cursor: not-allowed; }
    .btn--activate { background: var(--cokb); color: var(--cok); border: 1px solid var(--cok); }
    .btn--activate:hover { background: var(--cok); color: #fff; }
    /* State messages */
    .state-msg { padding: var(--sp6); text-align: center; font-size: 14px; color: var(--ct2); }
    .state-msg--error { color: var(--ce); }
    /* Modal */
    .modal-backdrop {
      position: fixed; inset: 0; background: rgba(0,0,0,.45);
      display: flex; align-items: center; justify-content: center; z-index: 400;
    }
    .modal {
      background: var(--cs0); border-radius: var(--r3);
      padding: var(--sp6); width: 100%; max-width: 480px;
      box-shadow: 0 8px 32px rgba(0,0,0,.18);
    }
    .modal--sm { max-width: 380px; }
    .modal-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--sp5); }
    .modal-title { font-size: 18px; font-weight: 600; color: var(--ct1); margin: 0; }
    .modal-close { background: none; border: none; font-size: 22px; cursor: pointer; color: var(--ct2); line-height: 1; }
    .modal-body { font-size: 14px; color: var(--ct2); margin: 0 0 var(--sp5); line-height: 1.6; }
    .modal-footer { display: flex; justify-content: flex-end; gap: var(--sp3); margin-top: var(--sp5); }
    .modal-error { background: var(--ceb); border: 1px solid var(--ce); border-radius: var(--r2); padding: var(--sp3); font-size: 13px; color: var(--ce); margin-bottom: var(--sp4); }
    /* Form (modal) */
    .row-2col { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp4); }
    .form-group { display: flex; flex-direction: column; gap: 4px; margin-bottom: var(--sp4); }
    .form-label { font-size: 13px; font-weight: 500; color: var(--ct1); }
    .req { color: var(--ce); }
    .form-input {
      height: 40px; border: 1px solid var(--cb); border-radius: var(--r2);
      padding: 0 var(--sp3); font-size: 14px; color: var(--ct1);
      background: var(--cs0); outline: none; transition: border-color .15s;
    }
    .form-input:focus { border-color: var(--cp); box-shadow: 0 0 0 3px var(--cps); }
    .form-input--error { border-color: var(--ce); }
    .form-error { font-size: 12px; color: var(--ce); }
    /* Toast */
    .toast {
      position: fixed; bottom: 24px; left: 50%; transform: translateX(-50%);
      background: var(--ct1); color: #fff;
      padding: 12px 20px; border-radius: var(--r2);
      font-size: 13px; z-index: 600;
      box-shadow: 0 4px 16px rgba(0,0,0,.25);
      animation: fadeInUp .2s ease;
    }
    @keyframes fadeInUp {
      from { opacity: 0; transform: translateX(-50%) translateY(8px); }
      to   { opacity: 1; transform: translateX(-50%) translateY(0); }
    }
  `]
})
export class UsersComponent implements OnInit, OnDestroy {
  users: AdminUser[] = [];
  filteredUsers: AdminUser[] = [];
  loading = true;
  loadError: string | null = null;

  searchQuery = '';
  roleFilter = '';
  statusFilter = '';

  // Create / Edit modal
  showUserModal = false;
  editTarget: AdminUser | null = null;
  userForm!: FormGroup;
  modalLoading = false;
  modalError: string | null = null;

  // Deactivate modal
  showDeactivateModal = false;
  deactivateTarget: AdminUser | null = null;
  deactivateError: string | null = null;

  // Toast
  toastMessage: string | null = null;
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private fb: FormBuilder,
    private adminService: AdminService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.buildForm();
    this.loadUsers();
  }

  ngOnDestroy(): void {
    if (this.toastTimer) { clearTimeout(this.toastTimer); }
  }

  private buildForm(user?: AdminUser): void {
    this.userForm = this.fb.group({
      firstName: [user?.firstName ?? '', [Validators.required, Validators.maxLength(100)]],
      lastName:  [user?.lastName  ?? '', [Validators.required, Validators.maxLength(100)]],
      email:     [user?.email     ?? '', [Validators.required, Validators.email]],
      role:      [user?.role      ?? 'staff', Validators.required]
    });
  }

  loadUsers(): void {
    this.loading = true;
    this.loadError = null;
    this.adminService.getUsers().subscribe({
      next: (data) => {
        this.users = data;
        this.applyFilters();
        this.loading = false;
      },
      error: (err: HttpErrorResponse) => {
        this.loading = false;
        this.loadError = err.status === 403
          ? 'You do not have permission to view users.'
          : 'Failed to load users. Please refresh.';
      }
    });
  }

  applyFilters(): void {
    const q = this.searchQuery.toLowerCase();
    this.filteredUsers = this.users.filter(u => {
      const matchSearch = !q ||
        `${u.firstName} ${u.lastName}`.toLowerCase().includes(q) ||
        u.email.toLowerCase().includes(q);
      const matchRole = !this.roleFilter || u.role === this.roleFilter;
      const matchStatus = !this.statusFilter ||
        (this.statusFilter === 'active' ? u.isActive : !u.isActive);
      return matchSearch && matchRole && matchStatus;
    });
  }

  onSearch(event: Event): void {
    this.searchQuery = (event.target as HTMLInputElement).value;
    this.applyFilters();
  }

  onRoleFilter(event: Event): void {
    this.roleFilter = (event.target as HTMLSelectElement).value;
    this.applyFilters();
  }

  onStatusFilter(event: Event): void {
    this.statusFilter = (event.target as HTMLSelectElement).value;
    this.applyFilters();
  }

  isSelf(user: AdminUser): boolean {
    const id = this.authService.getCurrentUserId();
    return id !== null && String(user.id) === id;
  }

  roleBadgeClass(role: string): string {
    return role === 'admin' ? 'badge badge--admin' : 'badge badge--staff';
  }

  statusBadgeClass(isActive: boolean): string {
    return isActive ? 'badge badge--active' : 'badge badge--inactive';
  }

  formatDate(iso: string | null): string {
    if (!iso) { return 'Never'; }
    return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  // Create modal
  openCreateModal(): void {
    this.editTarget = null;
    this.modalError = null;
    this.buildForm();
    this.showUserModal = true;
  }

  // Edit modal
  openEditModal(user: AdminUser): void {
    this.editTarget = user;
    this.modalError = null;
    this.buildForm(user);
    this.showUserModal = true;
  }

  closeUserModal(): void {
    this.showUserModal = false;
    this.editTarget = null;
  }

  mInvalid(field: string): boolean {
    const ctrl = this.userForm.get(field);
    return !!(ctrl?.invalid && ctrl.touched);
  }

  submitUserForm(): void {
    this.userForm.markAllAsTouched();
    if (this.userForm.invalid) { return; }
    this.modalError = null;
    this.modalLoading = true;

    if (this.editTarget) {
      const { firstName, lastName } = this.userForm.value as { firstName: string; lastName: string };
      this.adminService.updateUser(this.editTarget.id, { firstName, lastName }).subscribe({
        next: () => {
          this.modalLoading = false;
          this.showUserModal = false;
          this.showToast('User saved successfully.');
          this.loadUsers();
        },
        error: (err: HttpErrorResponse) => {
          this.modalLoading = false;
          this.modalError = this.extractError(err);
        }
      });
    } else {
      const { firstName, lastName, email, role } = this.userForm.value as {
        firstName: string; lastName: string; email: string; role: 'admin' | 'staff';
      };
      this.adminService.createUser({ firstName, lastName, email, role }).subscribe({
        next: () => {
          this.modalLoading = false;
          this.showUserModal = false;
          this.showToast('User saved successfully.');
          this.loadUsers();
        },
        error: (err: HttpErrorResponse) => {
          this.modalLoading = false;
          this.modalError = this.extractError(err);
        }
      });
    }
  }

  // Deactivate modal
  openDeactivateModal(user: AdminUser): void {
    this.deactivateTarget = user;
    this.deactivateError = null;
    this.showDeactivateModal = true;
  }

  closeDeactivateModal(): void {
    this.showDeactivateModal = false;
    this.deactivateTarget = null;
  }

  confirmDeactivate(): void {
    if (!this.deactivateTarget) { return; }
    this.modalLoading = true;
    this.deactivateError = null;
    const target = this.deactivateTarget;
    this.adminService.updateUser(target.id, { isActive: false }).subscribe({
      next: () => {
        this.modalLoading = false;
        this.showDeactivateModal = false;
        this.showToast(`${target.firstName} ${target.lastName} deactivated. Action recorded in audit log.`);
        this.loadUsers();
      },
      error: (err: HttpErrorResponse) => {
        this.modalLoading = false;
        this.deactivateError = err.status === 409
          ? 'Cannot deactivate the last active admin.'
          : this.extractError(err);
      }
    });
  }

  reactivate(user: AdminUser): void {
    this.adminService.updateUser(user.id, { isActive: true }).subscribe({
      next: () => {
        this.showToast(`${user.firstName} ${user.lastName} reactivated.`);
        this.loadUsers();
      },
      error: (err: HttpErrorResponse) => {
        this.showToast(this.extractError(err));
      }
    });
  }

  private extractError(err: HttpErrorResponse): string {
    return (err.error as { error?: string })?.error
      ?? (err.error as { message?: string })?.message
      ?? 'An unexpected error occurred.';
  }

  private showToast(msg: string): void {
    this.toastMessage = msg;
    if (this.toastTimer) { clearTimeout(this.toastTimer); }
    this.toastTimer = setTimeout(() => { this.toastMessage = null; }, 4000);
  }
}
