/**
 * Shared JWT test fixtures for AuthService and guard unit tests.
 * Pre-encoded values match the test data table in test_plan_fe_auth-service.md.
 */

function encodePayload(payload: object): string {
  return btoa(JSON.stringify(payload))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

/** Base64URL encoding of {"alg":"HS256"} */
const HEADER = 'eyJhbGciOiJIUzI1NiJ9';

/** {"sub":"1","role":"patient","exp":9999999999} — pre-encoded per test plan test data */
export const JWT_PATIENT_VALID =
  `${HEADER}.eyJzdWIiOiIxIiwicm9sZSI6InBhdGllbnQiLCJleHAiOjk5OTk5OTk5OTl9.sig`;

/** {"sub":"2","role":"staff","exp":9999999999} — pre-encoded per test plan test data */
export const JWT_STAFF_VALID =
  `${HEADER}.eyJzdWIiOiIyIiwicm9sZSI6InN0YWZmIiwiZXhwIjo5OTk5OTk5OTk5fQ.sig`;

/** exp: 1000000000 (Sep 2001) — always in the past, triggers isTokenExpired = true */
export const JWT_EXPIRED =
  `${HEADER}.${encodePayload({ sub: '1', role: 'patient', exp: 1_000_000_000 })}.sig`;

/** Unknown role claim — getCurrentRole() must return null */
export const JWT_UNKNOWN_ROLE =
  `${HEADER}.${encodePayload({ sub: '1', role: 'superuser', exp: 9_999_999_999 })}.sig`;

/** Only 2 parts — decodeToken() must return null */
export const JWT_MALFORMED = 'only.two';

/** Three parts, empty payload segment — decodeToken() must return null */
export const JWT_EMPTY_PAYLOAD = 'header..signature';

/** Non-base64 payload characters — decodeToken() must return null without throwing */
export const JWT_INVALID_B64 = 'header.!!!.signature';
