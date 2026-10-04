# MedNest Internal Medicine demo: clinical knowledge base with Zapier and an Azure Function

A portfolio demo for a fictional adult internal-medicine practice. Staff (medical assistants, nurses, community health workers) ask questions and get cited answers from the organization's own documents. Your existing SpecialistAI backend does the answering. Zapier and a small Azure Function wrap it in the day-to-day workflow.

```
site/index.html      Public company home page (no mention of the AI tool, just a Staff login button)
site/portal.html     Demo staff login + "MedNest Clinical Library" question page
site/case-study.html One-page write-up
site/config.js       API URL and key, Zapier hook URLs, demo login, starter questions
azure-function/IngestFromUrl.cs   New HTTP-triggered Function so Zapier can add PDFs to the library
zapier/ZAPIER-SETUP.md            Step-by-step build guide for three Zaps
```

## What is real and what is not
- **Built and browser-tested here:** the home page, demo login, question page (preview mode and a stand-in for your API), citations, feedback buttons, and the exact requests sent to your API and to Zapier.
- **Not tested, because I can't reach your accounts:** the connection to your deployed API, the three Zaps, and `IngestFromUrl.cs`. The C# was written without a .NET SDK, so it has not been compiled. Expect small fixes.
- **Zapier:** the Catch Hook trigger needs a paid plan (it isn't on the Free plan, per Zapier's documentation). Zaps can't be exported as files, so you build them from the guide.

## What happens
1. A staff member signs in (demo login) and asks a question.
2. The page calls your SpecialistAI `/chat` endpoint with the question and recent history, and shows the answer with its source documents. If the library doesn't cover the question, it says so instead of guessing, and flags it.
3. In the background the page posts events to two Zapier hooks:
   - **Zap 1** logs every question and feedback tap to a Google Sheet.
   - **Zap 2** alerts a clinical lead in Slack when a question is unanswered, marked not helpful, or sent on by the staff member.
4. **Zap 3** watches a Drive folder. A new PDF dropped there is sent to the `IngestFromUrl` Function, which places it in the blob container your ingestion Function already watches. The existing pipeline then indexes it.

## Setup
1. **Check your API first.** Confirm the deployed chat API answers, using whatever request your Angular UI sends. The page assumes `POST` with JSON `{ "message": "...", "history": [{"role": "user", "content": "..."}] }` and an `X-API-Key` header. If your field names differ, change `messageField`, `historyField` and `keyHeader` in `site/config.js`. The page reads `answer` and `sources` (with `documentTitle`, `sectionTitle`, `content`) from the response and also accepts common variants. If your response uses other names, send me a sample and I'll match it.
2. **Allow the page to call the API (CORS).** Add the origin you serve the page from (for example `http://localhost:8080`) to the API's CORS policy. Without this the browser blocks the call.
3. **Use a separate demo API key.** The key in `config.js` is visible to anyone who opens the page source. Create a dedicated key with low limits for the demo, and rotate it after you've recorded.
4. **Zapier:** follow `zapier/ZAPIER-SETUP.md`. Paste the two hook URLs into `config.js`.
5. **Azure Function:** add `IngestFromUrl.cs` to your SpecialistAI-Assistant-Function project, add the package `Azure.Storage.Blobs` if it isn't already there, set the app settings listed at the top of the file (`INGEST_CONTAINER` is required), and deploy. Test it:
   ```
   curl -X POST "https://specialistai-assistant-function-v2.azurewebsites.net/api/IngestFromUrl?code=YOUR_FUNCTION_KEY" -H "Content-Type: application/json" -d '{"fileUrl":"https://drive.google.com/uc?export=download&id=FILE_ID","fileName":"test-document.pdf"}'
   ```
   A good reply is `202` with `"status":"queued"`.
6. **Run the site:** `cd site && python3 -m http.server 8080`, open http://localhost:8080 and click **Staff login**. Demo account: `demo@mednest.test` / `nest123`. Change these in `config.js`.

Until `api.url` is set, the page runs in a labelled **Preview mode** with sample answers, so you can see the design first. Sample answers there are short placeholders and are marked "Preview sample". Add `?mock=1` to force preview mode.

## Healthcare notes
- Use public documents only (your WHO malaria corpus is fine). Never put patient data or private records in a public demo.
- The page states that it is reference only and does not replace clinical judgment. Keep that banner.
- The fourth starter question (diabetic foot ulcers) is deliberately outside a malaria-only library. It shows the "library does not cover this" path and fires the Slack alert. Swap it if you ingest more documents.

## Recording order (60 seconds)
Home page and Staff login (5s) > ask a covered question, show the cited sources (15s) > ask the uncovered one, show the flag (10s) > Slack alert and the Google Sheet log (15s) > drop a PDF in the Drive folder and show the Zap and the new document (15s).

## Not covered
Real authentication, per-user history, and the Python API rebuild. The login is for show only.
