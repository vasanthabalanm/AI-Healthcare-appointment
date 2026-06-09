import { ApplicationConfig } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { sessionInterceptor } from './core/interceptors/session.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes, withComponentInputBinding()),
    // authInterceptor attaches the Bearer token; sessionInterceptor handles
    // proactive refresh and 401/423 response side-effects.
    provideHttpClient(withInterceptors([authInterceptor, sessionInterceptor]))
  ]
};
