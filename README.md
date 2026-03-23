# Demo Passkey

Questo è un progetto ASP.NET Core v10 che dimostra l'utilizzo di [WebAuthn](https://developer.mozilla.org/en-US/docs/Web/API/Web_Authentication_API) per far accedere gli utenti con passkey.

Come **backend**, usa ASP.NET Code Identity per la gestione degli utenti. I dati degli utenti sono salvati in un database Sqlite presente nella cartella `/Data`. Nel file `Program.cs` del backend trovi tutti gli endpoint che contengono la logica server side.

Come **frontend**, usa un singolo file `index.html` che si trova nella directory `wwwroot`. Il file usa del semplice JavaScript. Non sono state usate né librerie né framework client-side.

## Provare la demo

Assicurati di aver installato [.NET SDK v10](https://get.dot.net/). Poi segui questi passi:

1. Avvia il progetto da VS o da VSCode o con il comando `dotnet run`;
2. Fai il login con email (`user@domain.ext`) e password (`Password1!`);
3. Clicca il bottone `Aggiungi passkey`. A questo punto dovrebbe presentarsi il prompt per creare una passkey, purché tu abbia un qualche dispositivo per impostare una passkey (hardware o software, es. un lettore di impronte digitali, pin di accesso, ecc...);
4. In elenco apparirà la passkey appena creata;
5. Fai il logout;
6. Fai di nuovo il login, questa volta cliccando "Accedi con passkey".

> È possibile aggiungere più di una passkey purché si abbiano dispositivi diversi.


## Se vi chiedono della privacy
WebAuthn è un meccanismo di autenticazione rispettoso della privacy. Nessun dato sensibile dell'utente viene divulgato. Anche se l'utente dovesse usare un lettore di impronte di digitali, i suoi dati biometrici resteranno sul client, cioè sul suo personale dispositivo, e non verranno inviati al server.

## Dettagli tecnici sul funzionamento delle passkey

Trovi spiegazioni dettagliate su questa pagina della MDN. C'è anche un diagramma di sequenza che mostra lo scambio di informazioni tra client e server.
[https://developer.mozilla.org/en-US/docs/Web/Security/Authentication/Passkeys](https://developer.mozilla.org/en-US/docs/Web/Security/Authentication/Passkeys)

Ad ogni modo, ecco un riassunto.

### Creazione di una passkey

1. Il client fa una richiesta al server per ottenere "opzioni" di creazione di una passkey. In questa demo, l'endpoint è `POST /passkeys/creation-options`. Identity genera le opzioni con il metodo `signInManager.MakePasskeyCreationOptionsAsync`. Le "opzioni" in questione contengono parametri come: una challenge, una chiave pubblica, gli algoritmi disponibili e l'id e il nome dell'utente. Identity include anche l'id dei dispositivi già usati per creare una passkey, così che non verranno mostrati di nuovo. Il server salva la challenge (questo non lo si vede nella demo perché [viene fatto internamente da Identity](https://github.com/dotnet/aspnetcore/blob/8bba19e82be93ec1deb1cd9e254f6b236c836c43/src/Identity/Core/src/SignInManager.cs#L525)).
2. Ottenute queste "opzioni", il client chiama la API JavaScript `navigator.credentials.create(...)`;
3. All'utente viene chiesto di creare una passkey usando uno dei dispositivi a sua disposizione (es. lettore di impronte, pin di accesso, ecc...);
4. Il client ottiene così un oggetto che contiene la risposta alla challenge e la passkey vera e propria. La invia al server (in questa demo l'endpoint è `POST /passkeys`). Il server verifica che la risposta alla challenge sia corretta chiamando `signInManager.PerformPasskeyAttestationAsync`. Se corretta, salva la passkey dell'utente nel database.

## Login con Passkey

> Affinché l'utente possa fare il login con passkey, è necessario che abbia aggiunto almeno una passkey.

1. Il client fa una richiesta al server per ottenere "opzioni" per il login con passkey. In questa demo, l'endpoint è `POST /passkeys/request-options`. Identity genera le opzioni con il metodo `signInManager.MakePasskeyRequestOptionsAsync`.
2. Ottenute queste "opzioni", il client chiama la API JavaScript `navigator.credentials.get(...)`;
3. All'utente viene chiesto di autenticarsi (es. poggiare il dito sul lettore per leggere l'impronta digitale);
4. Il client ottiene così un oggetto che contiene la risposta alla challenge e la passkey vera e propria. Lo invia al server (in questa demo l'endpoint è `POST /login-with-passkey`). Il server verifica che la risposta alla challenge sia corretta chiamando `signInManager.PerformPasskeyAssertionAsync`. Se corretta, emette il cookie di autenticazione.

## Esempi

Esempi di JSON scambiato tra client e server durante le operazioni di creazione passkey e login con passkey.

### Esempio creation options generate dal server
```json
{"rp":{"name":"localhost","id":"localhost"},"user":{"id":"MjBkYj...","name":"User1","displayName":"User1"},"challenge":"w_EQI6d...","pubKeyCredParams":[{"type":"public-key","alg":-7},{"type":"public-key","alg":-37},{"type":"public-key","alg":-35},{"type":"public-key","alg":-38},{"type":"public-key","alg":-39},{"type":"public-key","alg":-257},{"type":"public-key","alg":-36},{"type":"public-key","alg":-258},{"type":"public-key","alg":-259}],"timeout":300000,"excludeCredentials":[],"authenticatorSelection":{"residentKey":"preferred","requireResidentKey":false,"userVerification":"required"},"hints":[],"attestationFormats":[]}
```

### Credenziali client in risposta alle creation options

```json
{"authenticatorAttachment":"platform","clientExtensionResults":{},"id":"hnFv...","rawId":"hnFv...","response":{"attestationObject":"o2NmbX...","authenticatorData":"SZYN5YgOjG...","clientDataJSON":"eyJ0eX...","publicKey":"MFkwE...","publicKeyAlgorithm":-7,"transports":["hybrid","internal"]},"type":"public-key"}
```

### Esempio di request options per il login con passkey

```json
{"challenge":"em5ImI...","timeout":300000,"rpId":"localhost","allowCredentials":[],"userVerification":"required","hints":[]}
```

### Esempio di credenziali per il login in risposta alle request options

```json
{"id":"hnFv...","rawId":"hnFv...","type":"public-key","response":{"authenticatorData":"SZYN5....","clientDataJSON":"eyJ0eXBl...","signature":"MEQCIE...","userHandle":"MjBkYjM..."},"clientExtensionResults":{}}
```

## Nota su HTTPS
In produzione, l'uso delle passkey richiede HTTPS ma, per facilitare lo sviluppo, si possono usare su HTTP a patto che l'applicazione sia servita su `localhost`.

[https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0#https-requirement](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0#https-requirement)

Reminder: per installare il certificato di sviluppo di ASP.NET Core in locale, usa questo comando:
```csharp
dotnet dev-certs https --trust
```