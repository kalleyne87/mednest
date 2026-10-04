# Zapier setup: three Zaps around the SpecialistAI knowledge base

I can't create or test Zaps from here, so this is a step-by-step guide to build them in your account. Field names in Zapier's editor can differ slightly from what I describe, so use the test step in each Zap to confirm.

**Plan requirement (verified):** the Webhooks by Zapier "Catch Hook" trigger is not available on Zapier's Free plan, per [Zapier's webhook documentation](https://help.zapier.com/hc/en-us/articles/8496288690317-Trigger-Zaps-from-webhooks). Zaps 1 and 2 need a paid plan. Check Zapier's pricing page for any trial before you commit.

You need these before you start: a Google Sheet for logging, a Slack channel (suggested `#clinical-lead`) with your Slack app invited, and a Google Drive folder called `KB Inbox`.

---

## Zap 1: Log every question

**Purpose:** a running record of what staff ask, what was answered, and feedback.

1. **Trigger:** Webhooks by Zapier > **Catch Hook**. Copy the Custom Webhook URL into `site/config.js` as `zapier.logHook`.
2. Send a test so Zapier learns the fields. Run this once in a terminal (replace the URL):
   ```
   curl -X POST "YOUR_LOG_HOOK_URL" -d "event=question" -d "question=Test question" -d "answer=Test answer" -d "sources=Doc A | Doc B" -d "source_count=2" -d "answered=yes" -d "user=Jordan Ellis" -d "role=Medical Assistant" -d "timestamp=2026-10-04T12:00:00Z"
   ```
   Then click **Test trigger** in Zapier.
3. **Action:** Google Sheets > **Create Spreadsheet Row**. Use a sheet with these headers in row 1: `Time, User, Role, Event, Question, Answered, Sources, Answer, Feedback`. Map each from the hook fields: `timestamp`, `user`, `role`, `event`, `question`, `answered`, `sources`, `answer`, `feedback`.
4. Publish the Zap.

The page sends three kinds of events to this hook: `question` (every question), and `feedback` (when a staff member taps Helpful or Not helpful).

---

## Zap 2: Alert a clinical lead when the library can't help

**Purpose:** a person is told when a question went unanswered, an answer was marked not helpful, or a staff member pressed "Send to clinical lead".

1. **Trigger:** Webhooks by Zapier > **Catch Hook** (a second, separate hook). Copy its URL into `site/config.js` as `zapier.alertHook`.
2. Send a test:
   ```
   curl -X POST "YOUR_ALERT_HOOK_URL" -d "event=unanswered" -d "question=What is the protocol for diabetic foot ulcers?" -d "answer=I do not have information on that." -d "sources=" -d "source_count=0" -d "answered=no" -d "user=Jordan Ellis" -d "role=Medical Assistant, MedNest" -d "timestamp=2026-10-04T12:00:00Z"
   ```
3. **Action 1:** Slack > **Send Channel Message** to `#clinical-lead`. Suggested text:
   `Library gap ({{event}}): "{{question}}" asked by {{user}} ({{role}}).`
4. **Action 2 (optional):** Google Sheets > Create Spreadsheet Row on a second tab called `Needs review`, with the same columns plus a `Resolved` column that someone fills in.
5. Publish.

Event values sent here are `unanswered`, `not_helpful` and `escalated`.

---

## Zap 3: New document in Drive is added to the library

**Purpose:** a clinical lead drops a PDF into a shared folder and it is ingested without touching Azure.

First deploy `azure-function/IngestFromUrl.cs` into your SpecialistAI Function project (see the README) and get its URL and function key.

1. **Trigger:** Google Drive > **New File in Folder** > pick `KB Inbox`.
2. **Filter (recommended):** only continue if the file name ends with `.pdf`.
3. **Action:** Webhooks by Zapier > **POST**.
   - URL: `https://specialistai-assistant-function-v2.azurewebsites.net/api/IngestFromUrl?code=YOUR_FUNCTION_KEY`
   - Payload Type: **Json**
   - Data: `fileName` = the Drive file name, `fileUrl` = the file's download link from the trigger (use the field Zapier labels as the file or a download URL, then test).
4. Click **Test action**. A good response is `202` with `{"status":"queued", ...}`.
   - If it says **"Downloads from <host> are not allowed"**, add that host to the `ALLOWED_DOWNLOAD_HOSTS` app setting in Azure and test again. I could not verify which host Zapier's file links use, so expect to do this once.
   - **409** means a file with that name is already in the library.
5. **Action 2:** Slack > Send Channel Message: `New document added to the library: {{file name}}. Indexing has started.`
6. Publish.

After this Zap runs, your existing blob-triggered ingestion Function picks the PDF up and indexes it. For an ~530-chunk document set you measured about 5 minutes end to end, so wait a few minutes before asking questions about a new document.

---

## What the page sends (reference)

All fields are sent as form-encoded values:

| Field | Meaning |
|---|---|
| event | `question`, `feedback`, `unanswered`, `not_helpful`, `escalated` |
| question | What the staff member asked |
| answer | The answer text (first 600 characters) |
| sources | Document titles joined with ` | ` |
| source_count | How many sources were cited |
| answered | `yes` or `no` |
| feedback | `helpful` or `not helpful` (feedback events only) |
| user, role | Who asked |
| timestamp | ISO time |
