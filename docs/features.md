# Feature guide

How each feature works, its rules, its API and a short demo script, in the order the features were built (one
phase each; see [`plan.md`](../plan.md)). The [README](../README.md) has the overview, setup and diagrams;
[`decisions.md`](./decisions.md) the decisions; [`test-plan.md`](./test-plan.md) which test proves what.

## Service requests (Phase 3, cancellation added in Phase 5)

```
                     ┌──approve──► Approved ──assign──► Assigned
New ─────────────────┤
 │                    └──cancel───► Cancelled ◄──cancel──┘ (Manager only, once Approved)
 └────reject───► Rejected

Cancel from New: ServiceStaff, Admin or Manager (the creator-side roles undo their own mistake).
Cancel from Approved: Manager only.
```

| Who | Can |
|---|---|
| Admin, Manager, ServiceStaff | list and open requests (`GET /api/service-requests`, filters `status`, `priority`, `customerId`) |
| ServiceStaff, Admin | raise one (`POST /api/service-requests`) |
| Manager | approve (`POST /{id}/approve`), reject (`POST /{id}/reject`), cancel a New or Approved request (`POST /{id}/cancel`) |
| ServiceStaff, Admin | cancel their own New request (`POST /{id}/cancel`) |
| Technician, WarehouseStaff | nothing here (a Technician sees only their own work orders, below) |

- **BR-10:** the equipment must belong to the chosen customer, otherwise `400`. Retired equipment is also refused; Inactive and Under Maintenance equipment can be raised against.
- **Invalid moves are `409`.** Approving, rejecting or cancelling anything that is not in the right state returns a message such as *"This request is Approved and cannot be rejected. Only a New request can be approved or rejected."*, and the UI shows it.
- **Two Managers, one request.** Every mutating call (`approve`/`reject`/`cancel`/`assign`) sends back the record's `rowVersion`; the write is a single conditional `UPDATE … WHERE Status = @from AND RowVersion = @rowVersion`. If two decisions race — or a status change races an unrelated edit — exactly one changes a row and the other gets a `409` with "This record was changed by someone else. Refresh and try again.", so a decision is never silently overwritten.
- The creator always comes from the sign-in token, never from the request body.

**Try it (demo script).** After `docker compose up`, sign in at <http://localhost:8080>:

1. As `staff@sarawakbizops.local`: **Service Requests → New service request**. Pick *Rajang Timber Mills*, and note the equipment list contains only that customer's equipment. Describe the fault, set a priority, submit. You get a *Request #N submitted* notice.
2. As `manager@sarawakbizops.local`: **Service Requests** opens on the **New** requests. Open the one you raised (or the Urgent boiler request), and **Approve** it. Status flips to *Approved* at once and the buttons disappear.
3. Open another New request and choose **Reject**. The *Confirm rejection* button stays disabled until you type a reason, and the reason is then shown on the request.
4. Try to approve an already-approved request from a second browser tab: the page shows the 409 message and refreshes to the real state.
5. Open **Customers → Rajang Timber Mills → View & history**: the requests appear with their status, dates in Malaysia time (UTC+8), and link to their detail page.
6. As `warehouse@…` the Service Requests page is not offered and `/service-requests` bounces to the dashboard; as `technician@…` it bounces to **My Jobs** instead (see below).

## Work orders (Phase 4, cancellation and the lock guard added in Phase 5, PendingParts in Phase 7)

```
Approved ──assign──► Assigned ──start──► InProgress ──complete──► Completed ──approve──► Approved (locked)
                                                                      (report generated first)
 (request)            (atomic:         │   ▲    │  (technician)    (needs a                (Manager;
                       BR-01/11/12/13) │   │    │                   diagnosis and            read-only after)
                                       │ resume pending-parts       work performed, AC-05)
                                       │ (stock │
                                       │  back) ▼
                                       │  PendingParts
                                       │     │
                                       └─────┴──cancel──► Cancelled (locked; Manager only; issued parts go back to stock)
                                                  │
                                                  └─► the linked request reverts to Approved,
                                                       so it can be reassigned (BR-12's index
                                                       now excludes Cancelled rows)
```

| Who | Can |
|---|---|
| Manager | assign an Approved request to an active Technician (`POST /api/service-requests/{id}/assign`), approve a Completed job once its service report is generated (`POST /api/work-orders/{id}/approve`), cancel an Assigned/InProgress/PendingParts job (`POST /{id}/cancel`) |
| Admin, Manager, ServiceStaff | list and open any work order (`GET /api/work-orders`, `GET /{id}`), including its parts and RM total |
| Technician | see only their own jobs (`GET /api/work-orders/mine`), start (`POST /{id}/start`), save diagnosis and work performed (`PATCH /{id}/diagnosis`), add and return parts, pause for parts and resume (see *Parts used on a job* below), complete (`POST /{id}/complete`) |
| WarehouseStaff | nothing here |

- **BR-01/11/12/13, AC-01:** assignment is one atomic step — the request must be `Approved`, the technician must be active with the Technician role, and a request can produce at most one **live** work order (a filtered unique index — excluding `Cancelled` rows — is the final guard). A second assign attempt, or two Managers racing on the same request, always leaves exactly one work order; cancelling a job frees its request to be assigned again.
- **Approval needs the report (decided after Phase 15).** `approve` is a `400` — *"Generate the service report before approving this job."* — until the job has its report, and the condition is repeated in the approving `UPDATE`, so every Approved job has the report that approval locks. Who approved is recorded (`ApprovedByUserId`, shown on the timeline) next to when.
- **Cancelling returns the parts (decided after Phase 15).** In the same transaction as the cancel, every line still on the job goes back to stock with one `Return` ledger row each, recorded as the Manager's and noted *"Returned when work order #… was cancelled"*, exactly as if the technician had returned it — because the cancelled job is locked and nobody could return them afterwards. A part that really was used is written off by the warehouse with an Adjustment.
- **A request keeps every job it ever had.** `ServiceRequest → WorkOrders` is one-to-many: cancelled jobs stay with their request, and only one may be live (the filtered unique index). The request shows its live job (`workOrderId`) and its cancelled ones (`cancelledWorkOrderIds`), and links to both.
- **There is no `New` job.** Assigning creates the job as `Assigned`, so the design's `New` state was never produced; it was removed from the enum, the state machine, the dashboards' series and the UI.
- **BR-02, AC-02:** a technician acts only on their own job. Another technician's job is reported **404**, not 403, on every technician endpoint — its existence is not confirmed to someone who cannot open it. Office roles can read any job.
- **AC-05:** completion is rejected without a stored diagnosis and work performed. The technician UI has one button ("Complete job") that saves both fields and completes in the same tap, even though the API models them as two calls.
- **AC-07/AC-08, the central lock guard:** once a job is `Approved` or `Cancelled`, *every* mutating call refuses it with a `409` naming the lock — not just the status-changing ones. The diagnosis PATCH used to check this with its own inline `if`; it now goes through the same `WorkOrderStateMachine.IsLocked` check as everything else.
- **RowVersion conflicts:** every mutating call (`start`/`diagnosis`/`complete`/`approve`/`cancel`) sends back the job's `rowVersion`. This is what actually protects the diagnosis PATCH, which does not change status: two technicians (or two tabs) editing the same in-progress job both match `WHERE Status = 'InProgress'`, but only the one whose `RowVersion` still matches wins — the other gets the same "record changed — refresh" `409`.
- The Technician role gets its own **mobile-first view at `/tech`** (no office sidebar, 360 px layout, 44 px+ tap targets, a sticky primary action button) — chosen automatically the moment a Technician signs in or lands on `/`.

**Try it (demo script).** Continuing from the Service Requests walk above:

1. As `manager@sarawakbizops.local`: open **Service Requests**, filter to **Approved** and open the Centrifugal Pump request (the one seeded request left unassigned on purpose). Choose **Assign to a technician**, pick `Ahmad Bujang` (shows his current open-job count) and confirm. You land on a link to the new work order.
2. Sign in as `technician@sarawakbizops.local` on a narrow browser window (or your phone): you land on **My Jobs**, not the office dashboard. Open the pump job, tap **Start job**, type a diagnosis and what you did, then tap **Complete job** — one tap saves both fields and completes it.
3. Back as the Manager, open **Work Orders**, find the job you just completed: **Approve** stays disabled until you tap **Generate report**. Approve it then; it becomes read-only from then on and the timeline shows who approved it.
4. Try completing an in-progress job (there is a seeded one, the Air Compressor) with an empty diagnosis: the Complete button stays disabled until both fields are filled in, matching what the API would reject.
5. As `staff@sarawakbizops.local`, open the assigned pump request: no Assign button is offered, and a hint points at the Work Orders page instead.

**Cancellation and conflicts (Phase 5).**

1. As `staff@sarawakbizops.local`, raise a request, then open it and choose **Cancel request…** — type a reason and confirm. It moves straight to *Cancelled*; a Manager could have cancelled it too, but not another technician or warehouse account.
2. As `manager@sarawakbizops.local`, open the seeded **Assigned** job (Band Saw) and choose **Cancel job…**. Once confirmed, its request shows *Approved* again — open **Service Requests** and you can assign it to a technician a second time.
3. Open a work order's detail page: the **Timeline** panel lists every dated step (raised/assigned/started/completed/approved/cancelled) derived purely from its own timestamp columns — there is no separate history table.
4. Open the same work order in two browser tabs as the Manager, approve it in one, then click Approve in the other: the second tab shows the shared conflict banner ("This record was changed by someone else…") and reloads to the real state — the same banner every page uses for a `409`.

## Parts and stock (Phase 6)

```
Receive     (+q, warehouse)          ┐   one database transaction:
Adjustment  (±q, reason required)    ├─► 1. UPDATE Parts SET QuantityOnHand = QuantityOnHand + q
                                     │      WHERE Id = @id AND QuantityOnHand + q >= 0
Issue/Return  only from work-order   ┘   2. INSERT exactly one immutable InventoryTransaction
              parts (Phase 7, below)
Invariant: QuantityOnHand == SUM(ledger quantities) for every part, and never below zero.
```

| Who | Can |
|---|---|
| WarehouseStaff, Admin | create and edit parts (`POST /api/parts`, `PUT /api/parts/{id}`), receive and adjust stock (`POST /api/inventory-transactions`) |
| WarehouseStaff, Admin, Manager | list/search parts, one page at a time (`GET /api/parts?search=&includeInactive=&page=&pageSize=` → `{ items, totalCount, page, pageSize }`; 50 per page by default, 200 at most), open one (`GET /{id}`), see low stock (`GET /api/parts/low-stock`), read the ledger newest first (`GET /api/inventory-transactions`, filters `partId`, `type`, `workOrderId`, `fromDate`/`toDate` as MYT days or `from`/`to` as instants with an offset; keyset-paged with `limit` and `cursor` → `{ items, nextCursor }`) |
| Technician | search the **active** parts list only (`GET /api/parts`; `includeInactive` is ignored), to pick parts for their job — the picker asks the server for the top 20 matches as they type; no low stock, single part or ledger |
| ServiceStaff | nothing here |

- **BR-09, the ledger is the truth.** `QuantityOnHand` is a cached balance that only the inventory service moves, always in the same database transaction as exactly one new ledger row. There is **no PUT, PATCH or DELETE** for ledger rows (a test enumerates the app's routes to prove it), the API's own SQL login is denied UPDATE and DELETE on the ledger schema, and a trigger, `TR_InventoryTransactions_Immutable`, makes SQL Server refuse any UPDATE or DELETE on the table whoever sends it; a mistake is fixed with an offsetting Adjustment. Editing a part never touches its stock, and a new part starts at zero — its first delivery is a Receive.
- **Adjustments need a reason** (`400` without one) and can go either way; a Receive must be positive. Issue and Return are refused here (`400`): they only ever come from adding or returning parts on a work order.
- **Never below zero, even under concurrency.** The stock change is a single conditional `UPDATE … SET QuantityOnHand = QuantityOnHand + @q WHERE QuantityOnHand + @q >= 0`, so the database computes the balance from its current value and a movement that would go negative matches no row (`409` "Not enough stock: only N on hand"). Eight simultaneous write-offs racing for three units leave exactly three winners. A `CHECK (QuantityOnHand >= 0)` constraint is the database backstop.
- **Low stock (AC-09, API half):** active parts at or below their `MinimumStockLevel`, most urgent first. Inactive parts never appear, and cannot receive stock (their leftovers can still be written off). The Manager dashboard (Phase 11) counts and lists the same parts.
- Quantities are decimals (up to 3 places, for litres and metres); money is RM with 2 places. A value with more places is refused rather than silently rounded. Part edits carry the part's `rowVersion` like every other edit; a stock movement bumps it, so an edit made against a stale screen gets the usual `409`.
- The history's date filter takes UTC instants (`from` inclusive, `to` exclusive); the web app turns whole Malaysia-time days into that window.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `warehouse@sarawakbizops.local`: the sidebar shows **Inventory**. The list has 11 active parts with RM prices; four rows are highlighted **Low stock** (the conveyor belt is at 0 m, 10 below its minimum). Tick **Low stock only** to see just those four.
2. **New part** — e.g. `BRG-6206-2RS`, *Ball bearing 6206*, unit `pcs`, RM 21.90, minimum 10. It is saved with no stock and shows as low.
3. On that row choose **Receive**, enter `25` and a delivery note, and confirm: *Received 25 pcs… On hand now 25 pcs*, and the row is no longer low.
4. Choose **Adjust** on the same part, pick *Remove stock*, enter `2`: **Confirm adjustment** stays disabled until you type a reason. Then try removing `100`: the API refuses it — *Not enough stock: only 23 pcs on hand* — and nothing changes.
5. Open **History** on that row (or **Inventory → transaction history**): the receive and the adjustment, newest first, signed, with who did them and why. Filter by type or by a date range in Malaysia time. There is nothing to edit or delete.
6. As `manager@…` the Inventory pages are read-only (no New part, Receive, Adjust or Edit). As `staff@…` they are not offered and `/inventory` bounces to the dashboard; a technician lands on **My Jobs**.

## Parts used on a job (Phase 7)

```
POST /api/work-orders/{id}/parts  { partId, quantity, rowVersion }      — no price: it is snapshotted server-side
one database transaction:
  1. claim the job:  UPDATE WorkOrders SET Status = Status
                     WHERE Id = @id AND TechnicianId = @me AND Status = 'InProgress' AND RowVersion = @rv
  2. issue stock:    UPDATE Parts SET QuantityOnHand = QuantityOnHand - @q
                     WHERE Id = @part AND IsActive = 1 AND QuantityOnHand - @q >= 0     (0 rows → 409, AC-04)
  3. INSERT WorkOrderPart (UnitPrice = Part.UnitCost read under the row lock)
     INSERT exactly one Issue ledger row (-q, WorkOrderId = @id)                         (AC-03, BR-05)
DELETE /api/work-orders/{id}/parts/{lineId}  { rowVersion }  — the same claim, then the line is removed,
  the stock goes back up and exactly one Return ledger row (+q, WorkOrderId) is written.
```

| Who | Can |
|---|---|
| Technician (their own job, InProgress only) | add a part (`POST /api/work-orders/{id}/parts`), return a whole line (`DELETE /{id}/parts/{lineId}`), pause the job for parts (`POST /{id}/pending-parts` with a reason, the part and the quantity needed) |
| Technician (their own job, PendingParts) | resume it (`POST /{id}/resume`) once the awaited part has the awaited quantity on hand |
| Office roles | see the parts, RM line totals and job total on the work-order page, and the job's Issue/Return rows in the ledger (`?workOrderId=`) |

- **AC-03, atomic.** The line, exactly one Issue row and the stock decrease commit together or not at all. A test makes the database fail part-way (before the rows are written, and after they are written but before commit) and checks that stock, lines, ledger and even the job's `rowVersion` are exactly as before.
- **AC-04, BR-04.** Asking for more than is on hand is a `409` — *"Not enough stock: only 2 pcs of … on hand"* — and nothing changes.
- **Concurrency-safe (Open Question 8).** Issuing uses the same single conditional `UPDATE` as the warehouse: the database serialises everyone on the part's row and re-checks `QuantityOnHand - @q >= 0`, so N technicians racing for the last units get exactly as many successes as there is stock, never a negative balance, with no retry loop. The flagship test runs that race in five rounds (e.g. 8 technicians for 3 units, 8 for 2.5 units in 0.5s) and reconciles the ledger after each.
- **The job's `rowVersion` guards the job, not the stock.** Adding or returning a part carries the job's `rowVersion` (the Phase 5 rule) and bumps it. The claim holds the job row's lock until commit, so a Manager's cancel or the technician's own Complete cannot interleave, and a double-tap from the same screen adds the part once.
- **Price snapshot.** `UnitPrice` is copied from the catalogue at the moment of use; the client cannot send one, and a later price change never rewrites an existing line. Line totals are rounded to sen, and the job total is the sum of its lines.
- **Return** removes a whole line (to use fewer, return it and add the right amount). Parts can be added and returned only while the job is **InProgress**; an Approved or Cancelled job refuses with the central lock message. Cancelling a job with parts on it returns every line to stock (one Return row each, by the Manager); a part that was really used is written off with an Adjustment.
- **PendingParts.** The technician names the part and quantity they are waiting for and why. That is refused when the part is already in stock (add it instead). **Resume is guarded by stock:** the status change and the stock check are one conditional `UPDATE`, so it succeeds only when the awaited part has at least the awaited quantity on hand. Nothing is reserved (V2): the stock can still be used on another job before this one adds it. `start` and `resume` are separate actions — `start` cannot be used to skip the stock guard.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `technician@sarawakbizops.local` (a narrow window works best): open the in-progress **Air Compressor** job. **Parts used** already shows the overload relay (1 × RM 145.00). Tap **Add a part**, type `filter`, tap the *Air filter element*, use **+** to make it 2 and tap **Add to job**: the line and the new RM total appear.
2. Tap **Add a part** again, pick the *Automatic voltage regulator* (1 in stock) and press **+** to ask for 2: the picker says *Not enough stock: only 1 pcs on hand* and offers **Wait for this part…** instead of Add. (The API refuses the same thing with a `409`, and stock is unchanged.)
3. **Return** the air filter line: it disappears and the filters go back into stock.
4. Open the **Bottle Filling Line B** job — it is **Waiting for parts**: 6 m of conveyor belt, none in stock. Tap **Resume job**: it is refused with *Still waiting for parts: 6 m … needed, 0 on hand*.
5. As `warehouse@…`, **Inventory → Conveyor belt, 600 mm → Receive** 10 m. Back as the technician, **Resume job** now works; add the 6 m of belt, have the customer sign (Phase 8) and complete the job.
6. As `manager@…`, open any of these work orders: the parts table shows each line and the RM total, and the timeline shows *Waiting for parts* and *Resumed*. **Inventory → transaction history**, filtered by type *Issue*, shows each part used, linked to its job.

## Customer signature (Phase 8)

```
POST /api/work-orders/{id}/signature   multipart/form-data: file = the PNG from the canvas, rowVersion
  1. the bytes must really be a PNG (signature + IHDR checked; Content-Type and file name are ignored),
     at most 512 KB and 4000 px a side                                                      (else 400)
  2. owner + InProgress + not locked                                                (else 404 / 409)
  3. upload to blob storage under a NEW key:  work-orders/{id}/signature-{guid}.png
  4. UPDATE WorkOrders SET SignatureBlobKey = @key, SignedAt, SignatureSizeBytes
     WHERE Id = @id AND TechnicianId = @me AND Status = 'InProgress' AND RowVersion = @rv
       0 rows → delete the file just uploaded, 409
       1 row  → delete the previous signature's file (if the customer signed again)
GET  /api/work-orders/{id}/signature   → image/png (no-store, nosniff); the only way to the image
POST /api/work-orders/{id}/complete    → 400 until diagnosis, work performed AND a signature exist (BR-03, AC-06)
```

| Who | Can |
|---|---|
| Technician (their own job, InProgress only) | have the customer sign (`POST /{id}/signature`), sign again to replace it, see it |
| Admin, Manager, ServiceStaff | see the signature on the work-order page (`GET /{id}/signature`) |
| Another technician | nothing — the job and its signature read as 404 |
| WarehouseStaff | nothing (403) |

- **Storage (Open Question 2 → Azurite).** Files live in a private blob container behind `IFileStorage` (`SaveAsync`, `OpenReadAsync`, `DeleteAsync`); the implementation is `AzureBlobFileStorage` on the Azure Blob SDK, and Azurite plays Azure Storage in Docker, in CI and in the integration tests. The database keeps only the internal key and metadata — never the bytes and never a URL — so a signature can only be read through the authorized endpoint.
- **Storage and SQL cannot share a transaction**, so the order is chosen to fail safe: the file goes up first under a fresh key nothing points at, then one conditional `UPDATE` (the Phase 5 guard) switches the job over. A failed upload changes nothing; a lost `UPDATE` (stale `rowVersion`, double tap, the job moved on) deletes the file it just uploaded; a failed delete only leaves an orphaned file (logged, and deleted by the orphan sweep once it is a day old), never a job pointing at a missing one.
- **Completion.** `Complete` checks the signature after the diagnosis (so AC-05's message comes first) and repeats it in the `UPDATE`'s `WHERE`, so a job can never become Completed without one.
- **The pad is hand-rolled** (PRD D-06): a `<canvas>` with pointer events (touch, mouse and pen), Undo and Clear, `touch-action: none` so drawing does not scroll the page, sized to the device pixel ratio for crisp lines, and exported as a PNG on a white background.
- **`IFileStorage` fake.** `tests/…/Infrastructure/InMemoryFileStorage` is a dictionary-backed `IFileStorage`. `FileStorageContractTests` runs the same expectations against it (unit) and against `AzureBlobFileStorage` on the Azurite container (integration), so tests can swap the fake in — through `WithWebHostBuilder` — to see exactly which files exist or to make storage fail on purpose.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `technician@sarawakbizops.local` in a phone-sized window (or the browser's device toolbar at 360 px): open the in-progress **Air Compressor** job. The diagnosis is already there, but **Complete job** is disabled: *The customer must sign before the job can be completed.*
2. In **Customer signature**, sign in the box with the mouse or a finger. Try **Undo** and **Clear**, sign again and tap **Save signature**: the stored signature replaces the pad, and **Complete job** is enabled. (**Sign again** lets the customer redo it until the job is completed.)
3. Tap **Complete job**.
4. As `manager@…`, open that work order: **Customer signature** shows the image, the timeline shows *Customer signed*, and **Approve** is available. The seeded Completed (generator) and Approved (forklift) jobs carry a (fictional) signature too.

## Job photos (Phase 9)

The second file through the stack reuses the signature's storage path — `IFileStorage`, a fresh key per upload,
upload-then-record, authorized download — for a richer artifact that a job can have many of.

```
POST   /api/work-orders/{id}/photos            multipart/form-data: file = one photo, rowVersion
  1. the bytes must be a JPEG (FF D8 FF) or PNG (signature + IHDR); Content-Type and file name are ignored;
     at most 8 MB; a WebP (RIFF…WEBP) is refused with its own message                       (else 400)
  2. metadata stripped: EXIF (but its orientation), XMP, IPTC, comments, PNG text/time chunks and
     anything after the image; a file whose structure cannot be followed is refused        (else 400)
  3. owner + InProgress + not locked, fewer than 20 photos                         (else 404 / 409)
  4. upload the stripped bytes to blob storage under a NEW key:  work-orders/{id}/photo-{guid}.{jpg|png}
  5. one transaction: claim the job (no-op UPDATE … WHERE owner AND InProgress AND RowVersion = @rv
     AND fewer than 20 photos — holds the job row's lock, bumps rowVersion), INSERT WorkOrderPhotos
       claim lost → delete the file just uploaded, 409
DELETE /api/work-orders/{id}/photos/{photoId}  body: rowVersion — same claim, DELETE the row, then the file
GET    /api/work-orders/{id}/photos/{photoId}  → the image with the type recorded at upload (no-store, nosniff)
```

| Who | Can |
|---|---|
| Technician (their own job, InProgress only) | take or pick photos (`POST /{id}/photos`), remove them (`DELETE /{id}/photos/{photoId}`), see them |
| Technician (their own job, any later state) | see them — once the job is Completed its photos are part of the record and cannot be removed |
| Admin, Manager, ServiceStaff | see the gallery on the work-order page (`GET /{id}/photos/{photoId}`) |
| Another technician | nothing — the job and its photos read as 404 |
| WarehouseStaff | nothing (403) |

- **One photo per request, one after another.** Every upload carries and bumps the job's `rowVersion` (the Phase 5 rule), so the Photos step queues what the technician picked and sends it in order, each upload using the `rowVersion` the previous one returned; the page holds its other actions (Complete, parts, Remove) until the queue is done. A same-screen double tap has exactly one winner.
- **Upload progress** needs `XMLHttpRequest` (`fetch` cannot report upload progress): `apiUpload` in `api/client.ts` is the one call that uses it, and it turns failures into the same `ApiError` as every other call. A failed photo stays in the queue with the reason and **Retry** / **Discard**; a `409` reloads the job.
- **Camera or gallery.** **Take photo** is a file input with `capture="environment"` (opens the rear camera on a phone); **Choose photos** picks several from the gallery. A photo over 8 MB, a WebP (which the API refuses because a PDF cannot show it), or another type the API does not take but the browser can decode (HEIC on some iPhones), is redrawn in the browser as a JPEG of at most 2048 px a side before upload; otherwise photos go up untouched.
- **Removal is only before completion** (`WorkOrderStateMachine.AcceptsPhotoChanges`: InProgress only). A PendingParts job keeps its photos but must be resumed to change them, like parts. Photos are **not** required to complete a job (the signature is the rule-bearing evidence); cancelling keeps them.
- **No location or camera details are kept.** The API strips a photo's metadata before storing it and keeps only the EXIF orientation, so a portrait photo still shows upright; the image data itself is copied unchanged. The size shown is that of the stored, stripped file.
- **Served as what the bytes are.** The download uses the content type detected at upload, with `nosniff`, so nothing uploaded can come back as HTML or script.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `technician@sarawakbizops.local` in a phone-sized window: open the in-progress **Air Compressor** job. **Photos** already shows one (fictional) site photo.
2. Tap **Choose photos** and pick two images (or **Take photo** on a phone): each shows a progress bar, then joins the thumbnails. Try a text file renamed to `.jpg`: it is refused with *A photo must be a JPEG or PNG image.* — **Retry** or **Discard** it.
3. **Remove** one photo. Have the customer sign and tap **Complete job**: the photos stay, and the Remove buttons are gone.
4. As `manager@…`, open that work order: **Photos** shows the gallery; tap a thumbnail to open it full size. The seeded Completed (generator) and Approved (forklift) jobs carry two photos each.

## Service report (Phase 10)

The workflow's final artifact: a PDF built from the completed job and stored behind the same `IFileStorage` path as
signatures and photos.

```
POST /api/work-orders/{id}/generate-report     Manager; no body (it documents the job without changing it)
  1. the job must be Completed — or Approved without a report yet (once)           (else 404 / 409)
  2. first run: draw SR-{year}-{n} from the ServiceReportNumbers SEQUENCE; later runs keep the number, revision + 1
  3. build the report model from the job; read the signature and photos from storage; render the PDF
  4. upload it under a NEW key:  work-orders/{id}/report-{guid}.pdf
  5. one transaction: SELECT the job WITH (UPDLOCK) — an Approve waits, two generators take turns — re-check
     the state and that nobody generated meanwhile, then INSERT or UPDATE ServiceReports
       re-check fails → delete the file just uploaded, 409
       committed      → delete the previous rendering's file
GET  /api/work-orders/{id}/report   → application/pdf, "SR-….pdf" (no-store, nosniff); the only way to the PDF
GET  /api/reports                   → every report, newest first, with its job (office roles)
```

| Who | Can |
|---|---|
| Manager | generate and regenerate the report of a Completed job; download it; see the Reports page |
| Admin, ServiceStaff | download reports and see the Reports page, but not generate |
| Technician (their own job) | download their job's report (**View report** on the completed job) |
| Another technician | nothing — the job and its report read as 404 |
| WarehouseStaff | nothing (403) |

- **What the report shows** (PRD §6.7): report number and revision, who generated it and when, the customer (contact, phone, email, address), the equipment (serial, type, brand/model, location), technician, priority, the problem as reported, diagnosis, work performed, notes, the parts used with quantities, unit prices and RM totals, the job's timeline, photo thumbnails, and the customer's signature. Times are MYT (UTC+8), money is RM. The content is built by `ServiceReportBuilder` into a `ServiceReportModel` — a pure step, so the tests assert on the model rather than on PDF bytes — and then laid out by `ServiceReportRenderer` (A4, header and "Page x of n" footer on every page, text wrapped with the fonts' real metrics, short sections kept together).
- **Locking (BR-14 per D-05).** While the job is Completed the Manager may regenerate freely: the number stays, the revision goes up and the previous file is deleted. Approval locks the report; there is no reopen flow. A job approved *without* a report (e.g. approved before Phase 10 existed) may get exactly one — its data is already locked, so that report cannot differ from one made before approval. Since approval now requires the report, only such older jobs can be in that state.
- **Unique numbers under parallel generation.** `SR-{year}-{sequence}`: the sequence is a SQL Server `SEQUENCE`, so parallel generators never draw the same value and no table is locked for it; the year is the MYT year the number was first issued. A unique index on `ReportNumber` backs it up. A number drawn by a generation that then loses its race is simply skipped (numbers can have gaps).
- **No rowVersion.** Generating does not change the job, so it takes no conflict token and does not bump the job's `rowVersion` — the Manager can generate and then approve from the same page load. Instead the write re-checks the job under an `UPDLOCK`: an Approve that races a generation waits for it, and a generation that finds the job approved (or someone else's report) meanwhile gives up with a 409 and deletes its file.
- **Pictures.** PNG photos and the signature are decoded and scaled down to thumbnails while they stream (no full-size decode in memory); JPEG photos are embedded as they are, with their EXIF orientation applied, up to 16 MB of JPEGs per report; WebP photos stored before uploads stopped taking them, and any file missing from storage or unreadable, get a *"Not pictured in the PDF — open this photo in the app"* placeholder rather than failing the report.
- **Any script the font covers.** Latin text uses the standard Helvetica fonts, as before. Characters they cannot show (a customer or part name in Chinese, Greek or Cyrillic) are drawn with a subset of Noto Sans SC embedded in that report, holding just the glyphs it uses; the text can still be copied and searched in a PDF viewer. Scripts that need shaping (Arabic, Tamil, Thai) are not supported and print as `?`.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `manager@sarawakbizops.local`, open the seeded **Completed** work order (the generator). **Service report** says *No report yet*; tap **Generate report**: the report number, revision 1 and who generated it appear. **Open PDF** shows it in a new tab — customer, equipment, parts with RM totals, the two photos and the signature.
2. Tap **Regenerate report**: the same number, revision 2.
3. Tap **Approve**: the panel now says the report is *Locked*, and Regenerate is gone.
4. Open **Reports** in the sidebar: the seeded report (the forklift job, locked) and this one are listed; **Download** saves `SR-….pdf`.
5. As `technician@…` in a phone-sized window, open that job: **View report SR-…** opens the PDF.

## Manager dashboard (Phase 11)

The first analytics slice (PRD §6.9, SDD §14): the Manager's home page shows today's workload and the last six months,
every number computed by the API.

```
GET /api/dashboard/summary     Manager, Admin (Phase 12: WarehouseStaff and ServiceStaff get their own views, below)
  IAnalyticsService — named, parameterised operations, one grouped query each (the Phase 14 AI insights call the same ones):
    OpenRequestsByPriority()        ServiceRequests WHERE Status IN (New, Approved)          GROUP BY Priority
    WorkOrdersByStatus()            WorkOrders                                                GROUP BY Status
    JobsCompletedPerMonth(months)   WorkOrders WHERE CompletedAt in the MYT window
                                    AND Status IN (Completed, Approved)                       GROUP BY MYT year, month
    LowStockSummary(top)            Parts WHERE IsActive AND QuantityOnHand <= MinimumStockLevel
  AnalyticsRules (pure) — the MYT month window, zero-filling, fixed orders, the headline tiles
```

**Metric definitions** (also on `AnalyticsRules` in code):

| Metric | Definition |
|---|---|
| Open requests (by priority) | Service requests that still need someone to act before a job exists: **New** (awaiting review) or **Approved** (awaiting assignment — including one whose job was cancelled, which reverts it to Approved). Assigned requests are followed through their work order; Rejected and Cancelled are closed. Every priority is listed, most urgent first, zeros included. |
| Work orders (by status) | Every work order ever created, counted by its current status, **Cancelled included**; lifecycle order, zeros included. |
| Active jobs (tile) | Work orders that are **Assigned**, **InProgress** or **PendingParts** — the ones with a technician. |
| Awaiting your approval (tile) | Work orders that are **Completed** (not yet approved by a Manager). |
| Jobs completed per month | Work orders whose status is **Completed or Approved**, counted in the **Malaysia-time (UTC+8) calendar month of `CompletedAt`**, for the six months ending with the current one (oldest first, empty months as 0). A job completed at 23:30 MYT on 31 August is an August job even though it is 15:30 UTC. Approval does not move a job between months; a job can only be cancelled before it is completed, so none is ever counted. |
| Completed this month (tile) | The last (current) month of that series. |
| Low stock | **Active** parts with `QuantityOnHand ≤ MinimumStockLevel` — one shared rule (`StockLevels.IsLow`), so the count always equals `GET /api/parts/low-stock` (AC-09). *Out of stock* is the subset at zero. The five furthest below their minimum are listed. |

- **Charts are hand-rolled** (Open Question 6, PRD D-06): horizontal bars are an HTML table and the monthly columns an ordered list, sized with CSS, so every number is plain text for screen readers and tests, and there is no chart library. Empty series show a sentence instead of blank bars; the page has loading, error (with **Refresh**) states and works at phone width.
- **Indexes** (migration `DashboardIndexes`, reviewed): `IX_WorkOrders_Status` makes the status count an index-only scan; `IX_WorkOrders_CompletedAt` (with `Status` included) turns the monthly trend into a range seek. The request count uses the existing `IX_ServiceRequests_Status`. The parts table is small and its rule compares two columns of one row, so it has none.
- **Time.** "Now" comes from an injected `TimeProvider`, so the tests pin it; month boundaries are computed in MYT and converted to a UTC range, and the bucketing itself runs in SQL (`DATEPART(…, DATEADD(hour, 8, CompletedAt))`).
- **Demo data** now includes eight approved jobs (ten since Phase 13) completed over the previous four and a half months (signed, with their reports), so the trend has a shape; the centrifugal pump fails twice on purpose for Phase 12's repeated-failures metric.

**Try it (demo script).** Sign in at <http://localhost:8080> as `manager@sarawakbizops.local` — the dashboard is the home page:

1. The tiles read **5** open requests, **3** active jobs, **1** awaiting approval and **4** parts low on stock; each tile links to its list.
2. **Open requests by priority**: Urgent 1, High 2, Medium 2, Low 0 — compare with **Service Requests** filtered to New and to Approved.
3. **Work orders by status**: Assigned 1, In Progress 1, Pending Parts 1, Completed 1, Approved 9, Cancelled 1 — the **Work Orders** list has the same 14.
4. **Jobs completed per month**: ten jobs spread over the last five months; the current month is highlighted.
5. **Low stock** lists BLT-CNV-600 (0 of 10 m, highlighted) first — the same four parts as **Inventory → Low stock only**.
6. As the warehouse, receive 12 m of BLT-CNV-600 (its minimum is 10); back as the Manager, **Refresh**: 3 parts low, none out of stock.
7. *(Since Phase 12)* Sign in as `staff@…`: the home page is the service queue instead (below).

## Analytics depth and role dashboards (Phase 12)

The rest of PRD §6.9, and a dashboard for each office role (PRD §5). One endpoint, a **role-shaped payload**: the
server picks the view from the caller's role and tags it (`"view"`), and each payload carries only its own view's
numbers — the page never decides what a role may see.

```
GET /api/dashboard/summary
  Admin, Manager   → "Manager"       Phase 11's summary + period, the averages, repeated failures, top parts
  WarehouseStaff   → "Warehouse"     stock tiles, low stock (10), jobs waiting for parts, top parts
  ServiceStaff     → "ServiceQueue"  awaiting review / assignment, open by priority, oldest open, recently rejected
  Technician       → 403             (My Jobs is their home)
  Several roles    → the broadest view: Manager, then Warehouse, then ServiceQueue (AnalyticsRules.ViewFor)

  New named operations (each takes the period as N MYT months ending with the current one — 6 on the dashboard):
    AverageTimeToAssignment(months)                  AVG(DATEDIFF(second, request.CreatedAt, AssignedAt))
    AverageCompletionDuration(months)                AVG(DATEDIFF(second, StartedAt, CompletedAt))
    RepeatedFailuresByEquipment(months, min, top)    ServiceRequests GROUP BY EquipmentId HAVING COUNT(*) >= min
    TopPartsByUsage(months, top)                     WorkOrderParts GROUP BY PartId, ORDER BY SUM(Quantity × UnitPrice)
    JobsWaitingForParts(top)                         WorkOrders WHERE Status = PendingParts, with the part's stock
    RequestQueue(top, recentDays)                    New / Approved counts, oldest open, recently rejected
```

**Metric definitions** (continued; also on `IAnalyticsService` and `AnalyticsRules`):

| Metric | Definition |
|---|---|
| Period | The same six **Malaysia-time calendar months** as the completed-jobs trend, ending with the current one (`period.fromMonth`–`toMonth`), so every Manager number describes one period. |
| Average time to assignment | From a request being **raised** (`CreatedAt`) to its job being **assigned** (`AssignedAt`), averaged over **every job assigned in the period** — including one later cancelled (the assignment happened). A request reassigned after a cancel is measured again from when it was raised: that is how long the customer waited. In hours; *null* with nothing to measure. |
| Average completion duration | From a job being **started** to being **completed**, over jobs **Completed or Approved** whose `CompletedAt` is in the period. Time paused for parts counts — it is part of how long the job took. A job with no start recorded is left out. |
| Repeated failures | Equipment with **3 or more fault reports raised in the period**. A fault report is a request that is New, Approved or Assigned — **Cancelled** (raised in error, or resolved before review) and **Rejected** (turned down, e.g. not worth repairing) do not count. Most reports first, then the most recently reported; the count of all such equipment is given, the top five listed. The threshold is a parameter of the operation. |
| Top parts by usage | Parts used on jobs in the period (by when each line was added), ranked by **cost** at the price snapshotted on the line — quantities in different units (L, m, pcs) cannot be compared, costs can. Net of returns (a returned line is deleted). A cancelled job's parts count: that stock left the store. Top five, with quantity and job count. |
| Jobs waiting for parts (warehouse) | Jobs paused as **PendingParts**, the longest wait first, each with the part and quantity asked for and what is on hand; *in stock* when there is enough to resume (the Phase 7 resume rule). |
| Awaiting review / assignment (service queue) | Requests **New** / **Approved** — the two halves of *Open requests*. *Waiting longest* lists the five oldest by when they were raised. |
| Rejected recently (service queue) | Requests rejected in the **last 30 days** (by `RejectedAt`), the most recent first with the reason, so the desk can tell the customer. |

- **Averages are computed by SQL Server** (`AVG(CAST(DATEDIFF(second, …) AS float))` — the cast stops an integer average) and reported in hours to two decimals; the UI shows minutes, hours or days.
- **Indexes:** none added. The duration and parts queries range-seek the Phase 11 `IX_WorkOrders_CompletedAt` or scan tables of hundreds of rows; the NFR-03 latency check on the seed (median of five warm calls < 500 ms) covers the whole, larger summary.
- **Demo data:** the historical jobs now each have their own wait for assignment (2.4 hours to 4 days) and time on the job (6 hours to 2 days), so the averages have something to average.

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `manager@sarawakbizops.local`, below the Phase 11 charts: **Turnaround** — request → assigned **47.0 h** over 16 assignments, started → completed **22 h** over 12 jobs (with Phase 13's two extra history jobs).
2. **Repeated failures**: SBW-FIL-001 (filling line A: loose caps, bottle rejects, the fraying belt), KCC-CMP-002 (the compressor: overload trip, oil carry-over, short-cycling — since Phase 13) and SPO-PMP-002 (the pump: hot bearings, low flow, the seal leak), 3 reports each; the serial opens the equipment page with that history. The kiln's second request was cancelled, so it does not count.
3. **Top parts by usage**: SEAL-KIT-HYD RM 185.00, OIL-HYD-46 12 L RM 178.80, REL-OVL-25A RM 145.00, BRG-6205-2RS 2 pcs RM 36.80.
4. As `warehouse@…`: tiles **11** active parts, **4** low, **1** out of stock, **1** job waiting; job waiting for **6 m of BLT-CNV-600** with 0 on hand (*Short*). Receive 12 m in **Inventory**, **Refresh**: the job is *In stock* ("1 can resume now") and nothing is out of stock.
5. As `staff@…`: **3** awaiting review, **2** awaiting assignment, **1** rejected in the last 30 days; *Waiting longest* starts with the pump seal (14 days) and each request links to its page; *Recently rejected* shows the log conveyor with its reason.
6. Raise a new request as `staff@…` and **Refresh**: *Awaiting review* goes to 4. As the Manager, reject it with a reason: it moves to *Recently rejected*.

### Dashboard endpoints, period and caching

```
GET /api/dashboard/manager?months=6          Admin, Manager                    DashboardSummaryDto
GET /api/dashboard/warehouse?months=6        Admin, Manager, WarehouseStaff    WarehouseDashboardDto
GET /api/dashboard/service-queue             Admin, Manager, ServiceStaff      ServiceQueueDashboardDto
GET /api/dashboard/summary?months=6          the caller's own view, as above (the web app uses this one)
```

- **One shape per endpoint** in the OpenAPI document; `summary` stays for the web app, which shows whichever view the caller's role gets.
- **`months`** is the period in Malaysia-time calendar months ending with the current one: **1–24**, default **6**. Anything else (0, 25, `six`) is a **400** validation problem. The service queue has no period.
- **Cached briefly.** Each view and period is computed once per `Dashboard:CacheSeconds` (15 by default; `0` turns it off) and shared by everyone who gets that view. No payload is per-user. A payload can be up to that old, and *Updated …* on the page (`generatedAt`) says when it was computed. A failed computation is never cached.
- **Part-usage cost** rounds each line to sen before summing (`ROUND(Quantity × UnitPrice, 2)`, the same rule as `StockMovementRules.LineTotal`), so a part's cost agrees with the job totals it came from.
- **MYT day bounds** have one server helper, `AnalyticsRules.MytDayStartUtc`, used by the month windows and the inventory history's `fromDate`/`toDate` filters. The browser sends calendar days and never applies the offset itself.

## AI technician assistant (Phase 13)

PRD §6.10 (AI-01), SDD §14, NFR-09. On the Diagnosis step the technician taps **Get suggestions** and sees possible
causes, suggested checks, related earlier jobs on the same equipment and parts that may be needed, under the label
*AI-generated assistance — not a certified diagnosis*. **Copy to notes** puts them into the job's notes field, where
they can be edited; nothing is applied to the job, and the diagnosis stays the technician's own.

```
POST /api/ai/technician-assistant      Technician (own jobs only — another's is a 404), Manager
  { "workOrderId": 41, "observations": "optional, up to 1000 characters" }
  200  disclaimer, generatedBy, possibleCauses[] (High/Medium/Low), suggestedChecks[], relevantPastCases[], relevantParts[]
  503  the assistant is unavailable (provider error, timeout, or an answer that failed validation) — carry on without it
  429  asked too often (per user; Retry-After)
```

**Providers — configuration only.** Everything goes through one interface, `ILlmClient`
(`CompleteStructuredAsync(prompt, schema)`), chosen by the `Ai` section:

| `Ai:Provider` | What it is | Used by |
|---|---|---|
| `Mock` (default) | `MockLlmClient`: deterministic keyword rules over the same JSON context a model would get, answering in the same schema. Free, offline, no key. The UI names it *Demo mock assistant*. | development, all tests, CI, Docker |
| `OpenAICompatible` | `OpenAiCompatibleLlmClient`: hand-rolled `/chat/completions` over `HttpClient` (no SDK), structured output with a strict JSON schema (or plain JSON mode). Reaches Google Gemini's free tier, Groq, a local Ollama, OpenAI or Azure OpenAI. | a real deployment, by choice |

```bash
# Manual setup: the key only ever lives in user-secrets (or the Ai__ApiKey environment variable)
cd src/SarawakBizOps.Api
dotnet user-secrets set "Ai:Provider" "OpenAICompatible"
dotnet user-secrets set "Ai:BaseUrl"  "https://generativelanguage.googleapis.com/v1beta/openai/"
dotnet user-secrets set "Ai:Model"    "gemini-2.5-flash"
dotnet user-secrets set "Ai:ApiKey"   "<your key>"
# Docker: AI_PROVIDER, AI_BASE_URL, AI_MODEL and AI_API_KEY in your own .env (see .env.example)
```

Also configurable: `Ai:TimeoutSeconds` (20), `Ai:MaxOutputTokens` (1200), `Ai:PermitsPerMinute` (10 per user),
`Ai:ResponseFormat` (`json_schema` or `json_object`). A real provider with no base URL or model stops startup with a hint.

#### AI boundaries

- **Advice only, never a write.** The AI code path gets its own read-only data access (`AiReadOnlyData`): queries
  only, no tracking, and an interceptor that refuses `SaveChanges` and every non-`SELECT` command. Tests assert the
  job's RowVersion and every table count are unchanged after asking.
- **A bounded context.** The model sees only the one job the caller may read: its problem, status and priority, the
  diagnosis so far, the technician's observations, the equipment's type/brand/model/installation date, **that
  equipment's own** last five completed jobs (same customer, BR-10), and up to 40 active catalogue parts. Never a
  customer name, contact, address, serial number or location, and never another customer's jobs — whatever is typed.
  Every text and list is capped.
- **Free text is data, not instructions.** The system prompt is fixed and contains no user text; the problem and
  notes travel as JSON string values in the user message, and the prompt tells the model to treat them as data.
- **The answer is checked, not trusted.** It must match the schema and size limits; ids the model was not offered
  are dropped, so it cannot point at a job or part it was never shown; the facts shown beside each past case and part
  (dates, problem, diagnosis, stock) are read from the database, only the *why* is the model's. The disclaimer is set
  by the server on every answer, not left to the model.
- **Failure is graceful.** A provider error, a timeout or an unusable answer is a 503 the UI shows as "unavailable";
  the job carries on. Logs record the job id, provider, timing and our own reason — never the prompt, the answer, a
  provider's error body or the key.
- **Cost and abuse.** A fixed one-minute window per signed-in user (10 by default); provider replies are read up to
  256 KB.
- **No key or real provider response is in the repository**; tests and CI run on the mock and a fake HTTP handler.

**Demo data:** two more approved history jobs (ten in all): the air compressor tripped on overload four months ago,
and the band saw's guide bearings were noisy. So the compressor now has three fault reports and joins the repeated
failures, and the Phase 12 averages move a little (above).

**Try it (demo script).** Sign in at <http://localhost:8080>:

1. As `technician@sarawakbizops.local`, open the in-progress **KCC-CMP-002** job (the compressor that short-cycles
   and trips its overload relay). Under **AI assistant**, optionally type what you notice, and tap **Get suggestions**.
2. The answer leads with the amber label *AI-generated assistance — not a certified diagnosis*: **Motor protection
   tripping** (High), checks such as measuring the running current, the earlier job *"Air compressor trips on overload
   during the hot afternoon shift"* (the oil carry-over job on the same machine is left out as unrelated), and the
   overload relay REL-OVL-25A with its stock.
3. Tap **Copy to notes**: the suggestions appear in *Notes*, labelled, for you to edit. The diagnosis is untouched,
   and nothing is saved until you complete the job.
4. As `manager@…`, open the same work order: the Manager gets the same panel, with the earlier job linked.
5. The band saw (**RTM-BSW-001**): start it as the technician and ask — its earlier noisy-guide-bearings job comes up.

## AI manager insights (Phase 14)

PRD §6.10 (AI-02), **AC-10**: the Manager AI uses backend-computed data only. On **AI Insights** (sidebar, Manager
only) the Manager types a question or taps an example; the answer is a short summary *with the figures it was
written from shown beside it*.

```
GET  /api/ai/manager-insights/topics   Manager — the supported topics, each with an example question (no rate limit)
POST /api/ai/manager-insights          Manager
  { "question": "How many jobs were completed last month?" }      (1–300 characters)
  200  outcome "Answered": topic, periodLabel/period, definition, note, figures[] {label, value, unit},
       summary (null + summaryUnavailableReason when the model failed), disclaimer, generatedBy
  200  outcome "Refused": refusalReason, supportedTopics[]
  429  asked too often (the same per-user AI limit as the technician assistant)
```

**How a question is answered — the model never computes a metric.**

1. **Rules, not the model, read the question.** `ManagerInsightsIntentMapper` (pure, unit-tested) scores the
   question against each topic's weighted phrases and picks the best; no match, or a tie, is a refusal. It also reads
   the period: *this month*, *last month*, *last 3 months*, *past quarter*, *last year*, *this year*; nothing said is
   the dashboard's six months. A question the operations cannot answer truthfully is refused for what it is, even when
   a topic matches: a breakdown by technician or customer, a forecast, revenue/profit/pay, or a period shorter than a
   month (*today*, *last week*), or more than 24 months.
2. **An allow-listed operation computes the figures.** Each topic is one of the existing named
   `IAnalyticsService` operations (below), run through the AI's read-only data access (`AiReadOnlyData.Analytics`, the
   same guarded, no-tracking context as Phase 13). Totals and derived tiles use the dashboard's own rules
   (`AnalyticsRules`).
3. **The model words only those figures.** It is sent the topic, period, definition and figures as JSON — **not the
   question** and no raw rows, customer names or serial numbers (equipment appears as type and id). Its answer is one
   `summary` string.
4. **The summary is checked against the figures.** `ManagerInsightsOutput` refuses a summary containing any number
   that is not one of the figures' numbers (or that number rounded to fewer decimals, as in "47 hours" for 47.03):
   no sums, differences, percentages or conversions of its own. A refused summary, a provider error or a timeout does
   not fail the request — the Manager gets the figures with *"The AI summary is unavailable right now"*.

| Topic | Operation | Period | Example question |
|---|---|---|---|
| Jobs completed | `JobsCompletedPerMonth` | yes | How many jobs were completed last month? |
| Work orders by status | `WorkOrdersByStatus` | now | How many jobs are awaiting approval? |
| Open service requests | `OpenRequestsByPriority` | now | How many open requests are there by priority? |
| Low stock | `LowStockSummary` | now | Which parts are low on stock? |
| Time to assignment | `AverageTimeToAssignment` | yes | How long does it take to assign a request? |
| Completion duration | `AverageCompletionDuration` | yes | How long do jobs take to complete? |
| Repeated failures | `RepeatedFailuresByEquipment` (≥ 3 reports) | yes | Which equipment keeps failing? |
| Parts usage | `TopPartsByUsage` | yes | Which parts did we use the most in the last 3 months? |
| Jobs waiting for parts | `JobsWaitingForParts` | now | Which jobs are waiting for parts? |

"Last month" on a jobs-completed question is exactly that month. The averages, failures and parts usage are kept
for whole months up to today, so "last month" there covers last month *and this one*, and the answer says so; a
snapshot topic answers with current figures and says the period does not apply.

**AI boundaries** are Phase 13's (read-only data access, fixed system prompt with no user text, a checked answer,
graceful failure, logs without prompts or answers, the per-user rate limit, no key in the repository), plus: the
question never reaches the model, and the summary's numbers must be the figures' numbers.

**Try it (demo script).** As `manager@sarawakbizops.local`, open **AI Insights**:

1. Tap **How many jobs are awaiting approval?** — the summary (*Demo mock assistant*) reads out the figures, and the
   table beside it shows *Awaiting your approval (completed)* with the same number as the dashboard's tile.
2. Type *How many jobs were completed last month?* — one figure, for last month, matching that month's column on
   the dashboard chart.
3. Type *What was our revenue last month?* — refused: revenue is not recorded; the supported topics are listed, each
   example a click away. *Which technician completed the most jobs?* is refused as a breakdown the figures do not have.
