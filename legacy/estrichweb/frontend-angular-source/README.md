# Angular Source

Buildfähige Angular-Enterprise-Oberfläche für EstrichWeb.

```bash
npm install
npm run build
```

Der Produktionsbuild wird nach `../public-angular-build/browser` geschrieben. Für STRATO kann der Inhalt dieses Browser-Ordners nach `/public` kopiert werden. In dieser ZIP ist der aktuelle Build bereits in `/public` enthalten.

Module:

- Dashboard
- Baustellenakte
- Plantafel / Ressourcenplanung
- Aufgaben & Mängel
- Core API Service
- JWT/Tenant HTTP Interceptor
- Lazy Routes
- Mobile-first Layout
