# Hand-rolled cookie auth instead of ASP.NET Core Identity for the dashboard

The dashboard has exactly one real user class — the existing admin
account — with no self-registration, roles, external logins, or
password-recovery flows. ASP.NET Core Identity's schema and middleware
pipeline are built for those cases; adopting it would mean maintaining a
second, parallel user model alongside the existing koreader-protocol
`User` entity. Instead, dashboard login is a plain endpoint that checks
the existing `PasswordHash` and issues a cookie via
`CookieAuthenticationDefaults`.

## Consequences

- If the dashboard ever needs real multi-user features (accounts distinct
  from KOReader sync users, self-service password reset, etc.), this will
  need revisiting — Identity would be the natural fit at that point.
