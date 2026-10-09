# Offline support for technicians — plan

Status: **planned, not built.** Technicians work in plant rooms, basements and up-river sites where the signal comes
and goes. Today every step of a job is an API call, so a technician with no signal cannot record anything. This plan
lets them keep working on jobs already assigned to them and sends the work when the signal returns.

## Scope

**In:** the technician's pages only (`/tech/jobs`, `/tech/jobs/:id`), for jobs already assigned to them.

| Action | Offline? | Notes |
|---|---|---|
| See My Jobs and a job's detail | Yes | From the last copy seen online, marked with when it was saved. |
| Start, diagnosis/notes, pending parts, resume | Yes, queued | Replayed in order. |
| Photos | Yes, queued | The shrunk JPEG is kept as a Blob until it has uploaded. |
| Signature | Yes, queued | The PNG is kept as a Blob. |
| Add or return parts | Yes, queued, **may be refused on replay** | Stock is checked when the part is issued (BR-04). If the part ran out in the meantime, the technician is told and the job keeps its other changes. |
| Complete | Yes, queued, last in the job's queue | The server still enforces the signature rule (BR-03); if it refuses, the job stays InProgress and the technician is told why. |
| AI assistant | No | Needs the server; the button says so when offline. |

**Out:** the office pages. They are used at a desk on a stable connection and need current data from many users.

## Design

1. **App shell — service worker.** `vite-plugin-pwa` (Workbox) precaches the built HTML, JS and CSS so the app opens
   with no signal. API responses are **not** cached by the service worker: job data goes through the store below, so
   it is never served from an HTTP cache by mistake.
2. **Read cache — IndexedDB.** When My Jobs or a job loads online, the result is saved (`jobs` store, keyed by id,
   with `savedAt`). Offline, the pages read from it and show *Offline — last updated 10:42*. The copy is cleared on
   sign-out and when a job leaves the technician's list.
3. **Queued actions — an outbox in IndexedDB.** Each action is `{ id (UUID), jobId, kind, body, blob?, createdAt }`.
   While offline, an action is applied to the cached job straight away, so the page shows it at once as *waiting to
   send*, and is then appended to the outbox. One sender drains the outbox per job, in order, when `online` fires,
   when the app opens and every 30 s while items wait. It stops at the first failure so later steps never overtake an
   earlier one.
4. **Idempotency — server change.** Every queued action carries `Idempotency-Key: <outbox id>`. The API records the
   key with the result for 24 h and answers a repeat with the stored result. Without it, a request that reached the
   server but whose answer was lost (a common failure on a weak signal) would add the same part or photo twice. This is
   the only new server feature. It is one table, plus a filter on the technician endpoints.
5. **Conflicts.** A replayed action sends the `rowVersion` of the job as the technician last saw it, which is what
   the API checks today. If an office user changed the job in the meantime (for example, cancelled it), the replay gets
   a 409: the outbox for that job stops, the job is reloaded, and the technician sees what changed and which of their
   actions did not go through. Nothing is merged silently.
6. **Sessions.** The access token lasts 15 minutes and the refresh cookie 12 hours. Replaying needs a live session, so
   the sender refreshes first. If the session has ended, the outbox waits (it is per user, not lost) and the technician
   is asked to sign in, after which it sends. The outbox is never sent under a different user: it is keyed by user id
   and cleared only after it has been sent or the technician discards it.

## Order of work

1. Service worker and app shell. The app opens offline and shows *You are offline* (no data yet).
2. Read cache for My Jobs and job detail.
3. Idempotency keys in the API, with tests for repeated requests.
4. Outbox for the text actions (start, diagnosis, pending parts, resume, complete).
5. Photos and the signature in the outbox (Blobs, a storage-quota check, and a *waiting to send* thumbnail).
6. Parts in the outbox, with the refused-on-replay message.

Each step can ship on its own. The outbox is only switched on once idempotency keys are in place (step 3).

## Testing

- Vitest: the outbox (order, stop at the first failure, retry, per-user keys) against `fake-indexeddb`, and the pages
  in offline mode with a stubbed `navigator.onLine`.
- API: a repeated `Idempotency-Key` returns the first result and changes nothing; a different key does the work again.
- Playwright: go offline (`context.setOffline(true)`), record a diagnosis, a photo, a part and a signature, go back
  online, and check that the job on the server matches. The same run with an office cancel in between ends in the
  conflict message.

## Risks

- **Storage limits.** Browsers may evict IndexedDB under pressure. Photos are the large part: the outbox asks for
  persistent storage (`navigator.storage.persist()`) and warns when quota is low.
- **Long offline spells.** A job cancelled hours ago keeps collecting queued work. The conflict message covers this,
  but the wasted effort is real; the job page shows how old its copy is.
- **Stock.** Parts are the one action where offline use can be refused later. This is acceptable: the alternative is
  issuing stock that may not exist.
