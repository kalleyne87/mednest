// Everything you need to change lives in this file.
window.MEDNEST = {
  // 1) Your deployed SpecialistAI chat API (the /chat endpoint).
  //    Leave url empty to run in preview mode with sample answers.
  api: {
    url: 'https://specialistai-fastapi.livelydune-7816c230.eastus.azurecontainerapps.io/api/chat',  // e.g. https://specialistai-api.<env>.azurecontainerapps.io/api/chat
    key: 'kb-api-key-2026', // use a separate demo key, never your main one (it is visible in the page source)
    keyHeader: 'X-API-Key',
    messageField: 'message',      // name of the question field in the request body
    historyField: 'history'       // name of the conversation history field
  },

  // 2) Zapier "Catch Hook" URLs (Webhooks by Zapier, needs a paid Zapier plan). Leave empty to skip.
  zapier: {
    logHook: 'https://hooks.zapier.com/hooks/catch/29042848/4mg1u7l/', // Zap 1: every question is logged to a Google Sheet
    alertHook: 'https://hooks.zapier.com/hooks/catch/29042848/4mg9tut/'                 // Zap 2: unanswered, thumbs-down and "send to clinical lead" alerts
  },

  // 3) Demo login. NOT real security: it is visible in the page source.
  login: {
    email: 'kerwin.alleyne@gmail.com',
    password: 'test123',
    userName: 'Kerwin Alleyne',
    userRole: 'Super Admin',
    showHint: true
  },

  // 4) Starter questions. Change these to match the documents you ingested.
  suggested: [
    'What are the danger signs of severe malaria?',
    'When should malaria testing be done before starting treatment?',
    'What prevention advice should we give a patient travelling to a malaria area?',
    'What is the protocol for managing diabetic foot ulcers?'
  ]
};
