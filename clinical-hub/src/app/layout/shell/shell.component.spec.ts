import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { ShellComponent } from './shell.component';
import { AuthService } from '../../core/services/auth.service';

describe('ShellComponent — role-aware nav rendering (AC-004)', () => {
  let authSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    authSpy = jasmine.createSpyObj('AuthService', [
      'getCurrentRole', 'getToken', 'decodeToken', 'logout', 'isAuthenticated'
    ]);
    // Safe defaults — component must handle null gracefully
    authSpy.getToken.and.returnValue(null);
    authSpy.decodeToken.and.returnValue(null);

    TestBed.configureTestingModule({
      imports: [ShellComponent, RouterTestingModule, HttpClientTestingModule],
      providers: [{ provide: AuthService, useValue: authSpy }]
    });
  });

  function createFixture(role: 'patient' | 'staff' | 'admin' | null): ComponentFixture<ShellComponent> {
    authSpy.getCurrentRole.and.returnValue(role);
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    return fixture;
  }

  // TC-001: patient role — patient nav links are present in the DOM
  it('TC-001: patient nav links present in DOM when role is patient', () => {
    const fixture = createFixture('patient');
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('a[href="/patient/dashboard"]')).toBeTruthy();
    expect(el.querySelector('a[href="/patient/book"]')).toBeTruthy();
    expect(el.querySelector('a[href="/patient/appointments"]')).toBeTruthy();
    expect(el.querySelector('a[href="/patient/documents"]')).toBeTruthy();
  });

  // TC-002: staff role — staff nav links are present in the DOM
  it('TC-002: staff nav links present in DOM when role is staff', () => {
    const fixture = createFixture('staff');
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('a[href="/staff/schedule"]')).toBeTruthy();
    expect(el.querySelector('a[href="/staff/walkin"]')).toBeTruthy();
    expect(el.querySelector('a[href="/staff/queue"]')).toBeTruthy();
  });

  // TC-003: patient role — staff links are completely absent from DOM (not hidden)
  it('TC-003: staff nav links absent from DOM for patient role — not merely hidden', () => {
    const fixture = createFixture('patient');
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('a[href="/staff/schedule"]')).toBeNull();
    expect(el.querySelector('a[href="/staff/walkin"]')).toBeNull();
    expect(el.querySelector('a[href="/staff/queue"]')).toBeNull();
  });

  // currentRole signal reflects role correctly
  it('currentRole signal returns the injected role', () => {
    const fixture = createFixture('staff');
    expect(fixture.componentInstance.currentRole()).toBe('staff');
  });

  // null role — navItems is empty, no nav links rendered
  it('navItems is empty and no nav links rendered when role is null', () => {
    const fixture = createFixture(null);
    expect(fixture.componentInstance.navItems().length).toBe(0);
    const links = fixture.nativeElement.querySelectorAll('a.nav-item');
    expect(links.length).toBe(0);
  });
});
