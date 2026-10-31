# kosync-dotnet

> **AI-assisted development notice:** I forked the original kosync-dotnet repo with the intention of creating kosync server with a UI dashboard for my personal use. Ongoing development of this fork (the dashboard, sync-issue tracking, and related features) is being done with the assistance of [Claude Code]. If you have concerns about AI involvement in the software you run or contribute to, please take this into account before using or contributing to this project.

**kosync-dotnet** is a self-hostable implementation of the KOReader sync server built with .NET. It aims to extend the existing functionality of the official [koreader-sync-server](https://github.com/koreader/koreader-sync-server).

Users of KOReader can register a user on this synchronisation server and use the inbuilt _Progress sync_ plugin to keep all reading progress synchronised between devices.

All data is stored inside a [LiteDB](https://www.litedb.org/) database file.

## Origin & license

This is a fork of [jberlyn/kosync-dotnet](https://github.com/jberlyn/kosync-dotnet), extended with a web dashboard for tracking devices, diagnosing sync issues, book metadata, and more — see [docs/ROADMAP.md](docs/ROADMAP.md) for the plan. Licensed under [GPLv3](LICENSE), same as upstream.

## Web dashboard

Past the KOReader sync protocol itself, this fork adds a browser-based dashboard for keeping an eye on your library:

- **Devices** — every device that's synced, when it last synced, and the books on it.
- **Books** — every book across all your devices, with its overall progress shown as however far the furthest-along device has gotten.
- **Split Book** flags — when the same physical book ends up as two unrelated-looking entries because KOReader hashed it differently on different devices (common after Calibre re-converts a file). Merge them manually from the Books view once you spot it.
- **Progress Regression** flags — a device pushing a lower reading position than what's already recorded never silently overwrites your real progress; the blocked push still shows up in that book's history so you can see it happened.
- A **reset progress** action per document, for when you genuinely want to restart a book.

Log in at `/login` with the same admin account used for the management API below (username `admin`, password from `ADMIN_PASSWORD` or `admin` by default). The dashboard is a single-operator tool — there's no separate user system for it.

## How to run your own server?

The recommendation is to use [Docker](https://www.docker.com/) with [Docker Compose](https://docs.docker.com/compose/).

Published images are available for `linux/amd64` and `linux/arm64`; Docker automatically selects the correct image for the host architecture.

```
version: "3"
services:
  kosync:
    container_name: kosync
    image: ghcr.io/jberlyn/kosync-dotnet:latest
    restart: unless-stopped
    volumes:
      - </path/to/data>:/app/data
    environment:
      - ASPNETCORE_HTTP_PORTS=8080
      - ADMIN_PASSWORD=<super-strong-password>
      - TRUSTED_PROXIES=1.2.3.4, ::1, 10.0.0.0/24, 2001:db8::/32
    user: 1000:1000
```

`/app/data` will contain the database, which stores user and document progress information.

An admin user with the username `admin` will be created when the server first starts. `ADMIN_PASSWORD` sets that account's password **when it is created, and never again** — it is seed-only. If you do not provide a value, the password is set to `admin`. The recommendation is to set this to a strong password generated with a password manager like [Bitwarden](https://bitwarden.com/). This admin user can be used to interact with the management API.

This is a change in behaviour: **editing `ADMIN_PASSWORD` on an existing install now does nothing.** Earlier versions reapplied it on every startup, which meant a password you set yourself was silently reverted the next time the container restarted.

A password somebody else chose for you — the one `ADMIN_PASSWORD` seeds, or one an administrator sets when creating an account — is recorded as *provisional*: it identifies the account, and it is meant to be replaced by a password of your own rather than kept. An administrator can change their own password through `PUT /manage/users/password`, which no longer refuses admin accounts.

**While a password is provisional the account cannot sync.** `/users/auth`, `PUT /syncs/progress` and `GET /syncs/progress/{documentHash}` all refuse it with a `401`, so a fresh install syncs nothing until somebody has logged into the portal and replaced `admin`/`admin`. This is deliberate: it keeps a password an administrator typed from quietly becoming a device's long-lived credential. The accepted cost is that a new user has to visit the portal before their KOReader device will sync. See [ADR 0006](docs/adr/0006-forced-first-login-password-change.md).

**In the portal, a provisional password reaches nothing but the change-password screen.** Signing in at `/login` lands on `/change-password` instead of the dashboard, and every other page redirects back to it until the password has been replaced; signing out is the only other way off that screen. Replacing the password there grants both the portal and sync at once — they are the same stored credential.

Which is also the catch worth knowing before you do it: **changing your password signs out every KOReader device.** Each one keeps trying the old password until you open its sync settings and enter the new one. There is no way round that while one credential does both jobs, which is the trade [ADR 0006](docs/adr/0006-forced-first-login-password-change.md) made deliberately. The portal says so on the screen. You can also change your own password through `PUT /manage/users/password`, with the same consequence. The portal additionally requires a new password of at least 8 characters, configurable with `Passwords__MinimumLength`.

The sync refusal names the address to go and do that at, because KOReader shows the server's message and a bare refusal looks exactly like a wrong password. The server cannot work out its own public address — it is behind your reverse proxy, on your domain — so set `Portal__BaseUrl`:

```yaml
    environment:
      # Where your users reach the portal from. Defaults to
      # http://localhost:8080, which is only right if you are reading this
      # on the machine running the server. Deliberately not in
      # appsettings.json: one default, in one place.
      - Portal__BaseUrl=https://kosync.example.org
```

If you genuinely lose the admin password, set `ADMIN_PASSWORD_RESET` to any value and restart. On startup the admin's password goes back to `ADMIN_PASSWORD` (or `admin`), provisional again. It is acted on **once per value**, so the variable can stay in your compose file across later restarts without resetting anything again — which is the whole point, given that reset-on-every-boot is the behaviour removed above. To recover a second time, change it to a different value.

KOReader's own "Register" button, `POST /users/create`, is **disabled unless you turn it on**. With no configuration the server answers it with `User registration is disabled. Ask an Administrator to create your account.`, and accounts come from an administrator instead. A password somebody types into their own device is never provisional, so leaving device registration open would be a second way in that skips the forced first password change entirely, on a server meant to face the internet. See [ADR 0006](docs/adr/0006-forced-first-login-password-change.md).

To let devices register themselves anyway — a private server on your own network, say — set it explicitly:

```yaml
    environment:
      - SELF_REGISTRATION_ENABLED=true
```

This replaces `REGISTRATION_DISABLED`, which did the opposite job and defaulted to allowing registration. If you had it set, delete it: it is read by nothing now, and leaving it in place quietly leaves registration off. Neither variable exists in the official sync server.

The `TRUSTED_PROXIES` environment variable is an optional variable that defines one or more trusted proxies. It should be a comma delineated list of IPv4 or IPv6 addresses, or CIDR notation subnets (e.g. `10.0.0.0/24`, `10.1.2.3/32`, `2001:db8::/32`). If `TRUSTED_PROXIES` is set, when a request comes through a trusted proxy, the X-Forwarded-For header will be checked for the client's real IP address to use in logging. If `TRUSTED_PROXIES` is not set, or if a request does not come through a trusted proxy, the request's source IP address will be used for logging. Requests that do not come through a trusted proxy when `TRUSTED_PROXIES` is set will be marked with an asterisk (*) in the logs.

If you would like logging output to be on a single line, you may add the environment variable `SINGLE_LINE_LOGGING` and set it to `true`.

Mapping user to `1000:1000` is just a way to ensure the database files are created using your own user, rather than root. If your operating system user ID is different from `1000:1000`, change it to yours.

This example doesn't expose any ports for the container, however the sync server is accessible via port `8080` inside the container. The recommendation is to expose the server via a reverse proxy such as [Nginx Proxy Manager](https://nginxproxymanager.com/).

Prior to .NET 8, the default port was `80`. This was changed for [various reasons](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/aspnet-port) by Microsoft. You can manually set the port using the `ASPNETCORE_HTTP_PORTS` environment variable, if this is something you would like to configure.

### Running locally (e.g. to test against real KOReader devices)

For development, or to manually test against physical e-readers on your network, running directly with `dotnet run` is simpler than building a container image:

```
dotnet run
```

By default (`Properties/launchSettings.json`) this binds to `http://localhost:5181`, which is only reachable from the same machine. A KOReader device on your network needs the server bound to all interfaces instead:

```
dotnet run --urls http://0.0.0.0:5181
```

Then find this machine's LAN IP (`ip addr` / `ifconfig` on Linux, `ipconfig` on Windows) and, on the KOReader device, open the *Progress sync* plugin's settings, choose a custom server, and enter `http://<your-lan-ip>:5181` as the server address. Register or log in with a non-admin user there — the `admin` account is for the dashboard and management API, not for syncing a device's reading progress.

The SQLite database is created under a `data/` folder relative to wherever you run the app from; delete it to start from a clean slate between test runs.

## Management API

There are some management API endpoints you can interact with using a tool like [Postman](https://www.postman.com/).

Only the admin user can make requests to these API endpoints, with the exception of users being allowed to query and delete their own documents.

Deleting an account is an administrator's job, and not one they can do to themselves: `DELETE /manage/users` refuses self-deletion whoever asks, and refuses to remove the last remaining administrator. See [ADR 0006](docs/adr/0006-forced-first-login-password-change.md).

Reading a user's documents is the one thing being an admin does *not* unlock: `GET /manage/users/documents` only ever returns the caller's own documents, because an admin manages accounts but never reads another user's library. See [ADR 0004](docs/adr/0004-multi-tenant-portal.md).

All requests to these API endpoints require the following headers.

```json
{
  "x-auth-user": "admin"
  "x-auth-key": "<MD5 hash of your admin password>"
}
```

The MD5 on the wire is fixed by the KOReader protocol, but it is no longer what gets stored: passwords are kept as bcrypt over that MD5, so a copy of the database does not hand over a password-equivalent. Existing deployments need no action — stored credentials are upgraded in place on the next startup, with no password resets. See [ADR 0005](docs/adr/0005-bcrypt-over-md5-password-storage.md).

The bcrypt work factor and how long a verified credential is cached are configurable, in `appsettings.json` or as environment variables:

```yaml
    environment:
      # bcrypt cost. Higher is slower and stronger; 12 is the default.
      - Passwords__WorkFactor=12
      # How long an already-verified credential skips the hash on repeat
      # sync pushes. Set to 0 to verify every request.
      - Passwords__VerifiedCredentialCacheSeconds=300
```

### GET /manage/users

Returns a list of all users.

**Example Response**

```json
[
    {
        "id": 1,
        "username": "admin",
        "isAdministrator": true,
        "isActive": true,
        "documentCount": 0
    },
    {
        "id": 1,
        "username": "jberlyn",
        "isAdministrator": false,
        "isActive": true,
        "documentCount": 1
    }
]
```

### POST /manage/users

Creates a new user. This is the provisioning route, so `SELF_REGISTRATION_ENABLED` does not apply to it — it works whether or not devices may register themselves.

**Request Body**

```json
{
  "username": "jberlyn",
  "password": "super-strong-password"
}
```

**Example Response**

```json
{
  "message": "User created successfully"
}
```

### DELETE /manage/users?username=username

Deletes a user. The username to be deleted must be passed via a query parameter.

**Example Response**

```json
{
    "message": "Success"
}
```

### GET /manage/users/documents?username=username

Returns a list of your own documents and their sync status. Your username must be passed via a query parameter; any other username is refused with a `401`, for an admin as much as for anybody else.

**Example Response**

```json
[
    {
        "documentHash": "b8a9c5b494e7c91ece4cb8407e746ec7",
        "progress": "/body/DocFragment[28]/body/div/section/h1[1]/a/span/text().0",
        "percentage": 0.3186,
        "device": "Kobo_nova",
        "deviceId": "F47D5CA7123A4ABEAB90E8BF6F836356",
        "timestamp": "2023-05-23T11:43:00.225+10:00"
    },
    {
        "documentHash": "b1e5b67d6b0fe57ce9893d08dce65406",
        "progress": "/body/DocFragment[10]/body/div/h1/text().0",
        "percentage": 0.0166,
        "device": "Kobo_nova",
        "deviceId": "F47D5CA7123A4ABEAB90E8BF6F836356",
        "timestamp": "2023-05-23T11:44:34.165+10:00"
    }
]
```

### DELETE /manage/users/documents?username=username&documentHash=documentHash

Deletes a document for a user. The username for the user and the document hash must be passed via query parameters.

***Example Response***

```json
{
    "message": "Success"
}
```

### PUT /manage/users/active?username=username

Toggles the active status of a user. The username for the user must be passed via a query parameter. Users that are marked as inactive will not be able to login or push sync progress.

**Example Response**

```json
{
    "message": "User marked as inactive"
}
```

### PUT /manage/users/password?username=username

Updates the password for a user. The username for the user must be passed via a query parameter.

**Request Body**

```json
{
  "password": "super-strong-password"
}
```

**Example Response**

```json
{
    "message": "Password changed successfully"
}
```