// Wählt das Theme vor dem Start von UI5 (gespeicherte Wahl oder Systemeinstellung) – ohne Inline-Skript (CSP).
(function () {
  var themes = ['sap_horizon', 'sap_horizon_dark', 'sap_horizon_hcb', 'sap_horizon_hcw', 'sap_fiori_3', 'sap_fiori_3_dark',
    'sap_fiori_3_hcb', 'sap_fiori_3_hcw', 'sap_belize', 'sap_belize_plus', 'sap_belize_hcb', 'sap_belize_hcw'];
  var choice = 'auto';
  try {
    choice = window.localStorage.getItem('estrichone-hosting-theme') || 'auto';
  } catch (e) {
    choice = 'auto';
  }
  var theme = choice;
  if (themes.indexOf(choice) < 0) {
    var media = function (query) { return !!(window.matchMedia && window.matchMedia(query).matches); };
    var dark = media('(prefers-color-scheme: dark)');
    theme = media('(prefers-contrast: more)') || media('(forced-colors: active)')
      ? (dark ? 'sap_horizon_hcb' : 'sap_horizon_hcw')
      : (dark ? 'sap_horizon_dark' : 'sap_horizon');
  }
  window['sap-ui-config'] = { theme: theme };
})();
