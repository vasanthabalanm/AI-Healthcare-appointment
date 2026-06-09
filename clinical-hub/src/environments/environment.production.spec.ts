import { environment } from './environment.production';

describe('Production Environment (AC-005)', () => {
  // TC-004: apiBaseUrl must use https:// — no http:// origins in production bundle
  it('apiBaseUrl uses https:// scheme', () => {
    expect(environment.apiBaseUrl.startsWith('https://')).toBeTrue();
  });

  it('apiBaseUrl does not use http:// scheme', () => {
    expect(environment.apiBaseUrl.startsWith('http://')).toBeFalse();
  });

  it('production flag is true', () => {
    expect(environment.production).toBeTrue();
  });
});
