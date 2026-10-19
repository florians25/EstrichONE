export type FieldType = 'text' | 'textarea' | 'number' | 'date' | 'datetime' | 'boolean' | 'select' | 'json' | 'email' | 'phone';

export interface ModuleField {
  key: string;
  label: string;
  type: FieldType;
  required: boolean;
  options?: string[];
}

export interface EnterpriseModule {
  key: string;
  route: string;
  label: string;
  icon: string;
  category: string;
  table: string;
  permission: string;
  searchFields: string[];
  fields: ModuleField[];
}

export const ENTERPRISE_MODULES: EnterpriseModule[] = [
  {
    "key": "customers",
    "route": "kunden",
    "label": "Kunden",
    "icon": "business",
    "category": "Stammdaten",
    "table": "customers",
    "permission": "customers",
    "searchFields": [
      "name",
      "customer_no",
      "email",
      "city"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Name",
        "type": "text",
        "required": true
      },
      {
        "key": "contact_person",
        "label": "Ansprechpartner",
        "type": "text",
        "required": false
      },
      {
        "key": "customer_no",
        "label": "Kundennummer",
        "type": "text",
        "required": false
      },
      {
        "key": "email",
        "label": "E-Mail",
        "type": "email",
        "required": false
      },
      {
        "key": "phone",
        "label": "Telefon",
        "type": "phone",
        "required": false
      },
      {
        "key": "address",
        "label": "Adresse",
        "type": "text",
        "required": false
      },
      {
        "key": "zip",
        "label": "PLZ",
        "type": "text",
        "required": false
      },
      {
        "key": "city",
        "label": "Ort",
        "type": "text",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "contacts",
    "route": "ansprechpartner",
    "label": "Ansprechpartner",
    "icon": "contacts",
    "category": "Stammdaten",
    "table": "contacts",
    "permission": "customers",
    "searchFields": [
      "name",
      "email",
      "phone",
      "role"
    ],
    "fields": [
      {
        "key": "customer_id",
        "label": "Kunde-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Name",
        "type": "text",
        "required": true
      },
      {
        "key": "role",
        "label": "Funktion",
        "type": "text",
        "required": false
      },
      {
        "key": "email",
        "label": "E-Mail",
        "type": "email",
        "required": false
      },
      {
        "key": "phone",
        "label": "Telefon",
        "type": "phone",
        "required": false
      },
      {
        "key": "mobile",
        "label": "Mobil",
        "type": "phone",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "suppliers",
    "route": "lieferanten",
    "label": "Lieferanten",
    "icon": "local_shipping",
    "category": "Stammdaten",
    "table": "suppliers",
    "permission": "screed",
    "searchFields": [
      "name",
      "supplier_no",
      "contact_person",
      "email",
      "city"
    ],
    "fields": [
      {
        "key": "supplier_no",
        "label": "Lieferantennummer",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Lieferant",
        "type": "text",
        "required": true
      },
      {
        "key": "contact_person",
        "label": "Ansprechpartner",
        "type": "text",
        "required": false
      },
      {
        "key": "email",
        "label": "E-Mail",
        "type": "email",
        "required": false
      },
      {
        "key": "phone",
        "label": "Telefon",
        "type": "phone",
        "required": false
      },
      {
        "key": "address",
        "label": "Straße / Hausnummer",
        "type": "text",
        "required": false
      },
      {
        "key": "zip",
        "label": "PLZ",
        "type": "text",
        "required": false
      },
      {
        "key": "city",
        "label": "Ort",
        "type": "text",
        "required": false
      },
      {
        "key": "tax_no",
        "label": "Steuernummer / USt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "iban",
        "label": "IBAN",
        "type": "text",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "projects",
    "route": "projekte",
    "label": "Projekte",
    "icon": "domain",
    "category": "Projekte",
    "table": "projects",
    "permission": "projects",
    "searchFields": [
      "name",
      "project_no",
      "status",
      "city",
      "hinweise"
    ],
    "fields": [
      {
        "key": "customer_id",
        "label": "Kunde-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Projektname",
        "type": "text",
        "required": true
      },
      {
        "key": "project_no",
        "label": "Bauaktennummer",
        "type": "text",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "new",
          "planned",
          "in_progress",
          "delayed",
          "defect",
          "completed",
          "archived"
        ]
      },
      {
        "key": "address",
        "label": "Adresse",
        "type": "text",
        "required": false
      },
      {
        "key": "zip",
        "label": "PLZ",
        "type": "text",
        "required": false
      },
      {
        "key": "city",
        "label": "Ort",
        "type": "text",
        "required": false
      },
      {
        "key": "start_date",
        "label": "Start",
        "type": "date",
        "required": false
      },
      {
        "key": "end_date",
        "label": "Ende",
        "type": "date",
        "required": false
      },
      {
        "key": "budget_net",
        "label": "Budget netto",
        "type": "number",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      },
      {
        "key": "hinweise",
        "label": "Prominente Hinweise",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "project_phases",
    "route": "projektphasen",
    "label": "Projektphasen",
    "icon": "timeline",
    "category": "Projekte",
    "table": "project_phases",
    "permission": "projects",
    "searchFields": [
      "name",
      "status"
    ],
    "fields": [
      {
        "key": "project_id",
        "label": "Projekt-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "name",
        "label": "Phase",
        "type": "text",
        "required": true
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "planned",
          "in_progress",
          "done",
          "blocked"
        ]
      },
      {
        "key": "start_date",
        "label": "Start",
        "type": "date",
        "required": false
      },
      {
        "key": "end_date",
        "label": "Ende",
        "type": "date",
        "required": false
      },
      {
        "key": "progress_percent",
        "label": "Fortschritt %",
        "type": "number",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "construction_sites",
    "route": "baustellen",
    "label": "Baustellenakte",
    "icon": "apartment",
    "category": "Baustellen",
    "table": "construction_sites",
    "permission": "sites",
    "searchFields": [
      "name",
      "site_no",
      "status",
      "city",
      "site_manager",
      "hinweise"
    ],
    "fields": [
      {
        "key": "project_id",
        "label": "Projekt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "customer_id",
        "label": "Kunde-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Baustelle",
        "type": "text",
        "required": true
      },
      {
        "key": "site_no",
        "label": "Baustellennummer",
        "type": "text",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "new",
          "planned",
          "in_progress",
          "delayed",
          "defect",
          "completed",
          "archived"
        ]
      },
      {
        "key": "address",
        "label": "Adresse",
        "type": "text",
        "required": false
      },
      {
        "key": "zip",
        "label": "PLZ",
        "type": "text",
        "required": false
      },
      {
        "key": "city",
        "label": "Ort",
        "type": "text",
        "required": false
      },
      {
        "key": "site_manager",
        "label": "Bauleiter",
        "type": "text",
        "required": false
      },
      {
        "key": "phone",
        "label": "Telefon Baustelle",
        "type": "phone",
        "required": false
      },
      {
        "key": "site_email",
        "label": "E-Mail Baustellenkontakt",
        "type": "email",
        "required": false
      },
      {
        "key": "screed_type",
        "label": "Estrichart",
        "type": "select",
        "required": false,
        "options": [
          "Zementestrich",
          "Anhydritestrich",
          "Calciumsulfatestrich",
          "Heizestrich",
          "Schnellestrich",
          "Verbundestrich",
          "Industrieestrich",
          "Sichtestrich",
          "Trockenestrich"
        ]
      },
      {
        "key": "area_m2",
        "label": "Fläche m²",
        "type": "number",
        "required": false
      },
      {
        "key": "planned_start",
        "label": "Geplanter Start",
        "type": "date",
        "required": false
      },
      {
        "key": "planned_end",
        "label": "Geplantes Ende",
        "type": "date",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      },
      {
        "key": "hinweise",
        "label": "Prominente Hinweise",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "rooms",
    "route": "raeume",
    "label": "Räume",
    "icon": "meeting_room",
    "category": "Baustellen",
    "table": "rooms",
    "permission": "sites",
    "searchFields": [
      "name",
      "floor",
      "screed_type",
      "status"
    ],
    "fields": [
      {
        "key": "project_id",
        "label": "Auftrag-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "name",
        "label": "Raum",
        "type": "text",
        "required": true
      },
      {
        "key": "floor",
        "label": "Geschoss",
        "type": "text",
        "required": false
      },
      {
        "key": "area_m2",
        "label": "Fläche m²",
        "type": "number",
        "required": false
      },
      {
        "key": "screed_type",
        "label": "Estrichart",
        "type": "select",
        "required": false,
        "options": [
          "Zementestrich",
          "Anhydritestrich",
          "Calciumsulfatestrich",
          "Heizestrich",
          "Schnellestrich",
          "Verbundestrich",
          "Industrieestrich",
          "Sichtestrich",
          "Trockenestrich"
        ]
      },
      {
        "key": "height_mm",
        "label": "Aufbauhöhe mm",
        "type": "number",
        "required": false
      },
      {
        "key": "heating",
        "label": "Fußbodenheizung",
        "type": "boolean",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "prepared",
          "ready",
          "done",
          "blocked"
        ]
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "screed_types",
    "route": "estricharten",
    "label": "Estricharten",
    "icon": "layers",
    "category": "Estrich-Fachmodule",
    "table": "screed_types",
    "permission": "screed",
    "searchFields": [
      "name",
      "norm",
      "drying_time_days"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Estrichart",
        "type": "text",
        "required": true
      },
      {
        "key": "norm",
        "label": "Norm / Regelwerk",
        "type": "text",
        "required": false
      },
      {
        "key": "binder",
        "label": "Bindemittel",
        "type": "text",
        "required": false
      },
      {
        "key": "drying_time_days",
        "label": "Regeltrocknung Tage",
        "type": "number",
        "required": false
      },
      {
        "key": "min_thickness_mm",
        "label": "Mindestdicke mm",
        "type": "number",
        "required": false
      },
      {
        "key": "is_heated",
        "label": "Heizestrich geeignet",
        "type": "boolean",
        "required": false
      },
      {
        "key": "notes",
        "label": "Fachhinweise",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "screed_components",
    "route": "estrichkomponenten",
    "label": "Estrichaufbau-Komponenten",
    "icon": "construction",
    "category": "Estrich-Fachmodule",
    "table": "screed_components",
    "permission": "screed",
    "searchFields": [
      "name",
      "component_type"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Komponente",
        "type": "text",
        "required": true
      },
      {
        "key": "component_type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Dämmung",
          "Trittschalldämmung",
          "Folie",
          "Abdichtung",
          "Fußbodenheizung",
          "Heizrohre",
          "Randdämmstreifen",
          "Bewegungsfuge",
          "Dehnfuge",
          "Türbereich",
          "Rohrdurchführung"
        ]
      },
      {
        "key": "manufacturer",
        "label": "Hersteller",
        "type": "text",
        "required": false
      },
      {
        "key": "product_no",
        "label": "Artikelnummer",
        "type": "text",
        "required": false
      },
      {
        "key": "thickness_mm",
        "label": "Dicke mm",
        "type": "number",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "screed_buildups",
    "route": "estrichaufbau",
    "label": "Estrichaufbau je Raum",
    "icon": "view_in_ar",
    "category": "Estrich-Fachmodule",
    "table": "screed_buildups",
    "permission": "screed",
    "searchFields": [
      "name",
      "status"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Aufbau-Bezeichnung",
        "type": "text",
        "required": true
      },
      {
        "key": "insulation",
        "label": "Dämmung",
        "type": "text",
        "required": false
      },
      {
        "key": "impact_sound",
        "label": "Trittschalldämmung",
        "type": "text",
        "required": false
      },
      {
        "key": "foil",
        "label": "Folie / Trennlage",
        "type": "text",
        "required": false
      },
      {
        "key": "waterproofing",
        "label": "Abdichtung",
        "type": "text",
        "required": false
      },
      {
        "key": "underfloor_heating",
        "label": "Fußbodenheizung",
        "type": "boolean",
        "required": false
      },
      {
        "key": "edge_strip",
        "label": "Randdämmstreifen",
        "type": "boolean",
        "required": false
      },
      {
        "key": "movement_joints",
        "label": "Bewegungsfugen",
        "type": "textarea",
        "required": false
      },
      {
        "key": "pipe_penetrations",
        "label": "Rohrdurchführungen",
        "type": "textarea",
        "required": false
      },
      {
        "key": "total_height_mm",
        "label": "Gesamthöhe mm",
        "type": "number",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "draft",
          "checked",
          "approved",
          "installed",
          "changed"
        ]
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "inspection_templates",
    "route": "erstbegehung-vorlagen",
    "label": "Erstbegehung Vorlagen",
    "icon": "fact_check",
    "category": "Prüfung & Qualität",
    "table": "inspection_templates",
    "permission": "inspections",
    "searchFields": [
      "name",
      "type"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Vorlage",
        "type": "text",
        "required": true
      },
      {
        "key": "type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Erstbegehung",
          "Untergrundprüfung",
          "Heizestrich",
          "Abnahme",
          "Mangelprüfung"
        ]
      },
      {
        "key": "schema_json",
        "label": "Konfiguration JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "requires_photos",
        "label": "Fotopflicht",
        "type": "boolean",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "inspection_results",
    "route": "erstbegehung",
    "label": "Erstbegehungen & Checklisten",
    "icon": "playlist_add_check",
    "category": "Prüfung & Qualität",
    "table": "inspection_results",
    "permission": "inspections",
    "searchFields": [
      "status",
      "signed_by"
    ],
    "fields": [
      {
        "key": "template_id",
        "label": "Vorlage-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "in_progress",
          "passed",
          "failed",
          "signed",
          "archived"
        ]
      },
      {
        "key": "result_json",
        "label": "Ergebnis JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "signed_by",
        "label": "Unterschrieben von",
        "type": "text",
        "required": false
      },
      {
        "key": "signed_at",
        "label": "Unterschrieben am",
        "type": "datetime",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "measurements",
    "route": "pruefungen",
    "label": "Prüfungen allgemein",
    "icon": "speed",
    "category": "Prüfung & Qualität",
    "table": "measurements",
    "permission": "inspections",
    "searchFields": [
      "measurement_type",
      "status"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "measurement_type",
        "label": "Prüfung",
        "type": "select",
        "required": false,
        "options": [
          "Restfeuchte",
          "Ebenheit",
          "Höhenprüfung",
          "Temperatur",
          "Luftfeuchte",
          "Dämmung",
          "Untergrund"
        ]
      },
      {
        "key": "value_text",
        "label": "Messwert",
        "type": "text",
        "required": false
      },
      {
        "key": "unit",
        "label": "Einheit",
        "type": "text",
        "required": false
      },
      {
        "key": "measured_at",
        "label": "Gemessen am",
        "type": "datetime",
        "required": false
      },
      {
        "key": "measured_by",
        "label": "Gemessen von",
        "type": "text",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "ok",
          "not_ok",
          "recheck"
        ]
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "cm_measurements",
    "route": "cm-messungen",
    "label": "CM-Messungen",
    "icon": "opacity",
    "category": "Prüfung & Qualität",
    "table": "cm_measurements",
    "permission": "inspections",
    "searchFields": [
      "sample_location",
      "result_status"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "sample_location",
        "label": "Messstelle",
        "type": "text",
        "required": true
      },
      {
        "key": "sample_weight_g",
        "label": "Einwaage g",
        "type": "number",
        "required": false
      },
      {
        "key": "pressure_bar",
        "label": "Druck bar",
        "type": "number",
        "required": false
      },
      {
        "key": "moisture_percent",
        "label": "Restfeuchte %",
        "type": "number",
        "required": false
      },
      {
        "key": "temperature_c",
        "label": "Temperatur °C",
        "type": "number",
        "required": false
      },
      {
        "key": "humidity_percent",
        "label": "Luftfeuchte %",
        "type": "number",
        "required": false
      },
      {
        "key": "measured_at",
        "label": "Messzeit",
        "type": "datetime",
        "required": false
      },
      {
        "key": "result_status",
        "label": "Ergebnis",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "passed",
          "failed",
          "borderline"
        ]
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "planning_assignments",
    "route": "plantafel",
    "label": "Plantafel",
    "icon": "view_timeline",
    "category": "Disposition",
    "table": "planning_assignments",
    "permission": "planning",
    "searchFields": [
      "project_id",
      "title",
      "status",
      "notes"
    ],
    "fields": [
      {
        "key": "project_id",
        "label": "Auftrag / Projekt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "crew_id",
        "label": "Kolonne-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "vehicle_id",
        "label": "Fahrzeug-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "machine_id",
        "label": "Maschine-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Einplanung",
        "type": "text",
        "required": true
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "new",
          "planned",
          "in_progress",
          "delayed",
          "defect",
          "completed"
        ]
      },
      {
        "key": "start_at",
        "label": "Start",
        "type": "datetime",
        "required": true
      },
      {
        "key": "end_at",
        "label": "Ende",
        "type": "datetime",
        "required": true
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "planning_boards",
    "route": "plantafel-ansichten",
    "label": "Plantafel Ansichten",
    "icon": "dashboard_customize",
    "category": "Disposition",
    "table": "planning_boards",
    "permission": "planning",
    "searchFields": [
      "name",
      "view_type"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Name",
        "type": "text",
        "required": true
      },
      {
        "key": "view_type",
        "label": "Ansicht",
        "type": "select",
        "required": false,
        "options": [
          "Baustellen",
          "Kolonnen",
          "Fahrzeuge",
          "Maschinen",
          "Gantt",
          "Dispo-Board"
        ]
      },
      {
        "key": "date_from",
        "label": "Von",
        "type": "date",
        "required": false
      },
      {
        "key": "date_to",
        "label": "Bis",
        "type": "date",
        "required": false
      },
      {
        "key": "settings_json",
        "label": "Einstellungen JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "planning_conflicts",
    "route": "planungskonflikte",
    "label": "Planungskonflikte",
    "icon": "report_problem",
    "category": "Disposition",
    "table": "planning_conflicts",
    "permission": "planning",
    "searchFields": [
      "conflict_type",
      "status"
    ],
    "fields": [
      {
        "key": "assignment_id",
        "label": "Einplanung-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "conflict_type",
        "label": "Konflikt",
        "type": "select",
        "required": false,
        "options": [
          "crew_double_booked",
          "vehicle_double_booked",
          "machine_double_booked",
          "site_overbooked",
          "vacation",
          "sick_leave"
        ]
      },
      {
        "key": "severity",
        "label": "Schwere",
        "type": "select",
        "required": false,
        "options": [
          "info",
          "warning",
          "critical"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "accepted",
          "resolved"
        ]
      },
      {
        "key": "message",
        "label": "Meldung",
        "type": "textarea",
        "required": true
      },
      {
        "key": "resolved_at",
        "label": "Gelöst am",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "calendar_events",
    "route": "kalender",
    "label": "Kalender",
    "icon": "event",
    "category": "Disposition",
    "table": "calendar_events",
    "permission": "calendar",
    "searchFields": [
      "site_id",
      "project_id",
      "title",
      "visibility"
    ],
    "fields": [
      {
        "key": "project_id",
        "label": "Auftrag / Projekt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Termin",
        "type": "text",
        "required": true
      },
      {
        "key": "description",
        "label": "Beschreibung",
        "type": "textarea",
        "required": false
      },
      {
        "key": "start_at",
        "label": "Start",
        "type": "datetime",
        "required": true
      },
      {
        "key": "end_at",
        "label": "Ende",
        "type": "datetime",
        "required": true
      },
      {
        "key": "all_day",
        "label": "Ganztägig",
        "type": "boolean",
        "required": false
      },
      {
        "key": "event_type",
        "label": "Terminart",
        "type": "select",
        "required": false,
        "options": [
          "site_visit",
          "installation",
          "measurement",
          "acceptance",
          "office"
        ]
      },
      {
        "key": "color",
        "label": "Farbe",
        "type": "text",
        "required": false
      },
      {
        "key": "visibility",
        "label": "Sichtbarkeit",
        "type": "select",
        "required": false,
        "options": [
          "intern",
          "kunde",
          "subunternehmer",
          "öffentlich"
        ]
      },
      {
        "key": "recurrence_rule",
        "label": "Wiederholung RRULE",
        "type": "text",
        "required": false
      },
      {
        "key": "reminder_minutes",
        "label": "Erinnerung Minuten",
        "type": "number",
        "required": false
      },
      {
        "key": "external_provider",
        "label": "Sync Provider",
        "type": "select",
        "required": false,
        "options": [
          "none",
          "google",
          "outlook"
        ]
      },
      {
        "key": "external_id",
        "label": "Externe ID",
        "type": "text",
        "required": false
      }
    ]
  },
  {
    "key": "calendar_categories",
    "route": "kalender-kategorien",
    "label": "Kalender-Kategorien",
    "icon": "palette",
    "category": "Planung",
    "table": "calendar_categories",
    "permission": "calendar",
    "searchFields": [
      "label",
      "code"
    ],
    "fields": [
      {
        "key": "code",
        "label": "Code",
        "type": "text",
        "required": true
      },
      {
        "key": "label",
        "label": "Name",
        "type": "text",
        "required": true
      },
      {
        "key": "color",
        "label": "Farbe",
        "type": "text",
        "required": false
      },
      {
        "key": "icon",
        "label": "Icon",
        "type": "text",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      },
      {
        "key": "sort_order",
        "label": "Sortierung",
        "type": "number",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "crews",
    "route": "kolonnen",
    "label": "Kolonnen",
    "icon": "groups",
    "category": "Kolonnen & Ressourcen",
    "table": "crews",
    "permission": "crews",
    "searchFields": [
      "name",
      "status"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Kolonne",
        "type": "text",
        "required": true
      },
      {
        "key": "foreman_user_id",
        "label": "Vorarbeiter",
        "type": "text",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "active",
          "inactive",
          "vacation",
          "sick",
          "reserved"
        ]
      },
      {
        "key": "color",
        "label": "Farbe",
        "type": "text",
        "required": false
      },
      {
        "key": "capacity_m2_day",
        "label": "Kapazität m²/Tag",
        "type": "number",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "crew_members",
    "route": "kolonnen-mitarbeiter",
    "label": "Kolonnenmitglieder",
    "icon": "engineering",
    "category": "Kolonnen & Ressourcen",
    "table": "crew_members",
    "permission": "crews",
    "searchFields": [
      "name",
      "role"
    ],
    "fields": [
      {
        "key": "crew_id",
        "label": "Kolonne-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Name",
        "type": "text",
        "required": true
      },
      {
        "key": "role",
        "label": "Rolle",
        "type": "select",
        "required": false,
        "options": [
          "Vorarbeiter",
          "Kolonnenführer",
          "Estrichleger",
          "Helfer",
          "Subunternehmer"
        ]
      },
      {
        "key": "valid_from",
        "label": "Gültig von",
        "type": "date",
        "required": false
      },
      {
        "key": "valid_to",
        "label": "Gültig bis",
        "type": "date",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "vehicles",
    "route": "fahrzeuge",
    "label": "Fahrzeuge",
    "icon": "local_shipping",
    "category": "Kolonnen & Ressourcen",
    "table": "vehicles",
    "permission": "resources",
    "searchFields": [
      "name",
      "plate",
      "status"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Fahrzeug / Bezeichnung",
        "type": "text",
        "required": true
      },
      {
        "key": "plate",
        "label": "Kennzeichen",
        "type": "text",
        "required": false
      },
      {
        "key": "type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Transporter",
          "LKW",
          "Pumpe",
          "Anhänger",
          "PKW",
          "Sonstige"
        ]
      },
      {
        "key": "make",
        "label": "Marke",
        "type": "text",
        "required": false
      },
      {
        "key": "model",
        "label": "Modell",
        "type": "text",
        "required": false
      },
      {
        "key": "year_built",
        "label": "Baujahr",
        "type": "number",
        "required": false
      },
      {
        "key": "vin",
        "label": "Fahrgestellnummer (VIN)",
        "type": "text",
        "required": false
      },
      {
        "key": "mileage_km",
        "label": "Kilometerstand",
        "type": "number",
        "required": false
      },
      {
        "key": "fuel_type",
        "label": "Kraftstoff",
        "type": "select",
        "required": false,
        "options": [
          "Diesel",
          "Benzin",
          "Elektro",
          "Hybrid",
          "AdBlue/Diesel",
          "Sonstige"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "available",
          "planned",
          "maintenance",
          "defect",
          "inactive"
        ]
      },
      {
        "key": "holder",
        "label": "Halter",
        "type": "text",
        "required": false
      },
      {
        "key": "insurer",
        "label": "Versicherung",
        "type": "text",
        "required": false
      },
      {
        "key": "policy_no",
        "label": "Versicherungs-Nr.",
        "type": "text",
        "required": false
      },
      {
        "key": "insurance_due",
        "label": "Versicherung fällig",
        "type": "date",
        "required": false
      },
      {
        "key": "inspection_due",
        "label": "HU/Prüfung fällig",
        "type": "date",
        "required": false
      },
      {
        "key": "next_service",
        "label": "Nächster Service",
        "type": "date",
        "required": false
      },
      {
        "key": "purchase_date",
        "label": "Anschaffungsdatum",
        "type": "date",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "pumps",
    "route": "estrichpumpen",
    "label": "Estrichpumpen",
    "icon": "plumbing",
    "category": "Kolonnen & Ressourcen",
    "table": "pumps",
    "permission": "resources",
    "searchFields": [
      "name",
      "serial_no",
      "manufacturer",
      "status"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Pumpe / Bezeichnung",
        "type": "text",
        "required": true
      },
      {
        "key": "manufacturer",
        "label": "Hersteller",
        "type": "text",
        "required": false
      },
      {
        "key": "model",
        "label": "Modell",
        "type": "text",
        "required": false
      },
      {
        "key": "serial_no",
        "label": "Seriennummer",
        "type": "text",
        "required": false
      },
      {
        "key": "type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Estrichpumpe",
          "Fließestrichpumpe",
          "Mörtelpumpe",
          "Kompressor",
          "Sonstige"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "available",
          "planned",
          "maintenance",
          "defect",
          "inactive"
        ]
      },
      {
        "key": "max_output_l_min",
        "label": "Förderleistung (l/min)",
        "type": "number",
        "required": false
      },
      {
        "key": "max_pressure_bar",
        "label": "Förderdruck (bar)",
        "type": "number",
        "required": false
      },
      {
        "key": "max_distance_m",
        "label": "Förderweite (m)",
        "type": "number",
        "required": false
      },
      {
        "key": "hose_length_m",
        "label": "Schlauchlänge (m)",
        "type": "number",
        "required": false
      },
      {
        "key": "operating_hours",
        "label": "Betriebsstunden",
        "type": "number",
        "required": false
      },
      {
        "key": "year_built",
        "label": "Baujahr",
        "type": "number",
        "required": false
      },
      {
        "key": "inspection_due",
        "label": "Prüfung fällig",
        "type": "date",
        "required": false
      },
      {
        "key": "next_service",
        "label": "Nächster Service",
        "type": "date",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "machines",
    "route": "maschinen",
    "label": "Maschinen",
    "icon": "precision_manufacturing",
    "category": "Kolonnen & Ressourcen",
    "table": "machines",
    "permission": "resources",
    "searchFields": [
      "name",
      "serial_no",
      "status"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Maschine",
        "type": "text",
        "required": true
      },
      {
        "key": "serial_no",
        "label": "Seriennummer",
        "type": "text",
        "required": false
      },
      {
        "key": "type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Estrichpumpe",
          "Mischer",
          "Laser",
          "Schleifer",
          "Messgerät",
          "Sonstige"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "available",
          "planned",
          "maintenance",
          "defect",
          "inactive"
        ]
      },
      {
        "key": "inspection_due",
        "label": "Prüfung fällig",
        "type": "date",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "absences",
    "route": "urlaub-krank",
    "label": "Urlaub & Krankmeldungen",
    "icon": "event_busy",
    "category": "Kolonnen & Ressourcen",
    "table": "absences",
    "permission": "crews",
    "searchFields": [
      "absence_type",
      "status"
    ],
    "fields": [
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "crew_id",
        "label": "Kolonne-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Person/Kolonne",
        "type": "text",
        "required": true
      },
      {
        "key": "absence_type",
        "label": "Art",
        "type": "select",
        "required": false,
        "options": [
          "Urlaub",
          "Krank",
          "Schulung",
          "Frei",
          "Sonstiges"
        ]
      },
      {
        "key": "start_date",
        "label": "Von",
        "type": "date",
        "required": true
      },
      {
        "key": "end_date",
        "label": "Bis",
        "type": "date",
        "required": true
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "requested",
          "approved",
          "rejected",
          "active"
        ]
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "tasks",
    "route": "aufgaben",
    "label": "Aufgaben",
    "icon": "task_alt",
    "category": "Aufgaben & Mängel",
    "table": "tasks",
    "permission": "tasks",
    "searchFields": [
      "title",
      "status",
      "priority"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Aufgabe",
        "type": "text",
        "required": true
      },
      {
        "key": "description",
        "label": "Beschreibung",
        "type": "textarea",
        "required": false
      },
      {
        "key": "priority",
        "label": "Priorität",
        "type": "select",
        "required": false,
        "options": [
          "low",
          "medium",
          "high",
          "critical"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "assigned",
          "in_progress",
          "done",
          "archived"
        ]
      },
      {
        "key": "due_date",
        "label": "Frist",
        "type": "date",
        "required": false
      },
      {
        "key": "assigned_user_id",
        "label": "Verantwortlicher-ID",
        "type": "text",
        "required": false
      }
    ]
  },
  {
    "key": "task_comments",
    "route": "aufgaben-kommentare",
    "label": "Aufgaben-Kommentare",
    "icon": "comment",
    "category": "Aufgaben & Mängel",
    "table": "task_comments",
    "permission": "tasks",
    "searchFields": [
      "body"
    ],
    "fields": [
      {
        "key": "task_id",
        "label": "Aufgabe-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "body",
        "label": "Kommentar",
        "type": "textarea",
        "required": true
      },
      {
        "key": "created_by_name",
        "label": "Autor",
        "type": "text",
        "required": false
      },
      {
        "key": "is_internal",
        "label": "Intern",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "defects",
    "route": "maengel",
    "label": "Mängelmanagement",
    "icon": "bug_report",
    "category": "Aufgaben & Mängel",
    "table": "defects",
    "permission": "defects",
    "searchFields": [
      "title",
      "status",
      "severity"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Mangel",
        "type": "text",
        "required": true
      },
      {
        "key": "description",
        "label": "Beschreibung",
        "type": "textarea",
        "required": false
      },
      {
        "key": "severity",
        "label": "Schwere",
        "type": "select",
        "required": false,
        "options": [
          "low",
          "medium",
          "high",
          "critical"
        ]
      },
      {
        "key": "status",
        "label": "Workflow",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "assigned",
          "in_progress",
          "fixed",
          "review",
          "closed"
        ]
      },
      {
        "key": "assigned_user_id",
        "label": "Verantwortlicher-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "due_date",
        "label": "Frist",
        "type": "date",
        "required": false
      },
      {
        "key": "closed_at",
        "label": "Geschlossen am",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "defect_comments",
    "route": "maengel-kommentare",
    "label": "Mängel-Kommentare",
    "icon": "forum",
    "category": "Aufgaben & Mängel",
    "table": "defect_comments",
    "permission": "defects",
    "searchFields": [
      "body"
    ],
    "fields": [
      {
        "key": "defect_id",
        "label": "Mangel-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "body",
        "label": "Kommentar",
        "type": "textarea",
        "required": true
      },
      {
        "key": "created_by_name",
        "label": "Autor",
        "type": "text",
        "required": false
      },
      {
        "key": "is_customer_visible",
        "label": "Für Kunde sichtbar",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "documents",
    "route": "dokumente",
    "label": "Dokumentenmanagement",
    "icon": "folder",
    "category": "Dokumentation",
    "table": "documents",
    "permission": "documents",
    "searchFields": [
      "name",
      "category",
      "original_name"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "name",
        "label": "Dokumentname",
        "type": "text",
        "required": true
      },
      {
        "key": "original_name",
        "label": "Originaldatei",
        "type": "text",
        "required": false
      },
      {
        "key": "mime_type",
        "label": "MIME-Type",
        "type": "text",
        "required": false
      },
      {
        "key": "size_bytes",
        "label": "Größe Bytes",
        "type": "number",
        "required": false
      },
      {
        "key": "storage_path",
        "label": "Speicherpfad",
        "type": "text",
        "required": false
      },
      {
        "key": "category",
        "label": "Kategorie",
        "type": "select",
        "required": false,
        "options": [
          "general",
          "plan",
          "contract",
          "photo",
          "inspection",
          "acceptance",
          "invoice",
          "lexware"
        ]
      },
      {
        "key": "ocr_text",
        "label": "OCR Volltext",
        "type": "textarea",
        "required": false
      },
      {
        "key": "is_customer_visible",
        "label": "Für Kunde sichtbar",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "document_versions",
    "route": "dokument-versionen",
    "label": "Dokumentversionen",
    "icon": "history",
    "category": "Dokumentation",
    "table": "document_versions",
    "permission": "documents",
    "searchFields": [
      "version_no",
      "original_name"
    ],
    "fields": [
      {
        "key": "document_id",
        "label": "Dokument-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "version_no",
        "label": "Version",
        "type": "number",
        "required": false
      },
      {
        "key": "original_name",
        "label": "Datei",
        "type": "text",
        "required": false
      },
      {
        "key": "mime_type",
        "label": "MIME-Type",
        "type": "text",
        "required": false
      },
      {
        "key": "size_bytes",
        "label": "Größe Bytes",
        "type": "number",
        "required": false
      },
      {
        "key": "storage_path",
        "label": "Speicherpfad",
        "type": "text",
        "required": false
      },
      {
        "key": "change_note",
        "label": "Änderungsnotiz",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "photos",
    "route": "fotos",
    "label": "Fotodokumentation",
    "icon": "photo_camera",
    "category": "Dokumentation",
    "table": "photos",
    "permission": "photos",
    "searchFields": [
      "caption",
      "category"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "caption",
        "label": "Bildtext",
        "type": "text",
        "required": false
      },
      {
        "key": "category",
        "label": "Kategorie",
        "type": "select",
        "required": false,
        "options": [
          "before",
          "during",
          "after",
          "defect",
          "measurement",
          "acceptance",
          "general"
        ]
      },
      {
        "key": "original_path",
        "label": "Originalpfad",
        "type": "text",
        "required": false
      },
      {
        "key": "compressed_path",
        "label": "Komprimierter Pfad",
        "type": "text",
        "required": false
      },
      {
        "key": "taken_at",
        "label": "Aufgenommen am",
        "type": "datetime",
        "required": false
      },
      {
        "key": "gps_lat",
        "label": "GPS Lat",
        "type": "number",
        "required": false
      },
      {
        "key": "gps_lng",
        "label": "GPS Lng",
        "type": "number",
        "required": false
      },
      {
        "key": "annotations_json",
        "label": "Markierungen JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "is_customer_visible",
        "label": "Für Kunde sichtbar",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "reports",
    "route": "berichte",
    "label": "Berichte & Tagesberichte",
    "icon": "summarize",
    "category": "Dokumentation",
    "table": "reports",
    "permission": "reports",
    "searchFields": [
      "title",
      "report_type",
      "status"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "title",
        "label": "Bericht",
        "type": "text",
        "required": true
      },
      {
        "key": "report_type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Tagesbericht",
          "Wochenbericht",
          "Prüfbericht",
          "Mangelbericht",
          "KI-Bericht",
          "Projektanalyse"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "draft",
          "generated",
          "reviewed",
          "signed",
          "sent",
          "archived"
        ]
      },
      {
        "key": "content",
        "label": "Inhalt",
        "type": "textarea",
        "required": false
      },
      {
        "key": "generated_pdf_path",
        "label": "PDF Pfad",
        "type": "text",
        "required": false
      },
      {
        "key": "signed_by",
        "label": "Unterschrieben von",
        "type": "text",
        "required": false
      },
      {
        "key": "signed_at",
        "label": "Unterschrieben am",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "acceptances",
    "route": "abnahmen",
    "label": "Abnahmen",
    "icon": "verified",
    "category": "Dokumentation",
    "table": "acceptances",
    "permission": "quality",
    "searchFields": [
      "site_id",
      "status",
      "customer_name"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "room_id",
        "label": "Raum-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "customer_name",
        "label": "Kunde / Abnehmer",
        "type": "text",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "scheduled",
          "accepted",
          "accepted_with_defects",
          "rejected",
          "archived"
        ]
      },
      {
        "key": "accepted_at",
        "label": "Abgenommen am",
        "type": "datetime",
        "required": false
      },
      {
        "key": "signature_path",
        "label": "Unterschrift Pfad",
        "type": "text",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "quality_protocols",
    "route": "qualitaetsmanagement",
    "label": "Qualitätsmanagement",
    "icon": "workspace_premium",
    "category": "Prüfung & Qualität",
    "table": "quality_protocols",
    "permission": "quality",
    "searchFields": [
      "title",
      "status"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": true
      },
      {
        "key": "title",
        "label": "Protokoll",
        "type": "text",
        "required": true
      },
      {
        "key": "protocol_type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Prüfprotokoll",
          "Freigabe",
          "Reklamation",
          "Gewährleistung",
          "Nachprüfung"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "draft",
          "in_review",
          "approved",
          "rejected",
          "archived"
        ]
      },
      {
        "key": "content_json",
        "label": "Inhalt JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "approved_by",
        "label": "Freigegeben von",
        "type": "text",
        "required": false
      },
      {
        "key": "approved_at",
        "label": "Freigegeben am",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "warranty_cases",
    "route": "gewaehrleistung",
    "label": "Gewährleistung & Reklamation",
    "icon": "gavel",
    "category": "Prüfung & Qualität",
    "table": "warranty_cases",
    "permission": "quality",
    "searchFields": [
      "title",
      "status",
      "customer_name"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "customer_name",
        "label": "Kunde",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Fall",
        "type": "text",
        "required": true
      },
      {
        "key": "description",
        "label": "Beschreibung",
        "type": "textarea",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "open",
          "investigation",
          "accepted",
          "rejected",
          "resolved",
          "closed"
        ]
      },
      {
        "key": "due_date",
        "label": "Frist",
        "type": "date",
        "required": false
      },
      {
        "key": "cost_estimate_net",
        "label": "Kostenschätzung netto",
        "type": "number",
        "required": false
      }
    ]
  },
  {
    "key": "customer_portal_items",
    "route": "kundenportal",
    "label": "Kundenportal",
    "icon": "supervisor_account",
    "category": "Portale",
    "table": "customer_portal_items",
    "permission": "portal",
    "searchFields": [
      "title",
      "status"
    ],
    "fields": [
      {
        "key": "customer_id",
        "label": "Kunde-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Portal-Eintrag",
        "type": "text",
        "required": true
      },
      {
        "key": "portal_type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Dokument",
          "Status",
          "Termin",
          "Abnahme",
          "Mangelmeldung"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "visible",
          "hidden",
          "pending",
          "closed"
        ]
      },
      {
        "key": "payload_json",
        "label": "Daten JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "expires_at",
        "label": "Gültig bis",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "subcontractor_orders",
    "route": "subunternehmerportal",
    "label": "Subunternehmerportal",
    "icon": "handshake",
    "category": "Portale",
    "table": "subcontractor_orders",
    "permission": "portal",
    "searchFields": [
      "title",
      "status",
      "company_name"
    ],
    "fields": [
      {
        "key": "site_id",
        "label": "Baustelle-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "company_name",
        "label": "Subunternehmer",
        "type": "text",
        "required": true
      },
      {
        "key": "title",
        "label": "Auftrag",
        "type": "text",
        "required": true
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "new",
          "accepted",
          "in_progress",
          "reported",
          "checked",
          "closed"
        ]
      },
      {
        "key": "scope",
        "label": "Leistungsumfang",
        "type": "textarea",
        "required": false
      },
      {
        "key": "due_date",
        "label": "Frist",
        "type": "date",
        "required": false
      },
      {
        "key": "documents_required",
        "label": "Dokumente erforderlich",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "notifications",
    "route": "benachrichtigungen",
    "label": "Benachrichtigungen",
    "icon": "notifications",
    "category": "Kommunikation",
    "table": "notifications",
    "permission": "notifications",
    "searchFields": [
      "title",
      "channel",
      "status"
    ],
    "fields": [
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "title",
        "label": "Titel",
        "type": "text",
        "required": true
      },
      {
        "key": "body",
        "label": "Nachricht",
        "type": "textarea",
        "required": false
      },
      {
        "key": "channel",
        "label": "Kanal",
        "type": "select",
        "required": false,
        "options": [
          "app",
          "browser_push",
          "email",
          "sms",
          "whatsapp_business"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "queued",
          "sent",
          "failed",
          "read",
          "archived"
        ]
      },
      {
        "key": "send_at",
        "label": "Senden am",
        "type": "datetime",
        "required": false
      },
      {
        "key": "meta_json",
        "label": "Meta JSON",
        "type": "json",
        "required": false
      }
    ]
  },
  {
    "key": "notification_templates",
    "route": "nachrichten-vorlagen",
    "label": "Nachrichten-Vorlagen",
    "icon": "mark_email_read",
    "category": "Kommunikation",
    "table": "notification_templates",
    "permission": "notifications",
    "searchFields": [
      "name",
      "channel"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Vorlage",
        "type": "text",
        "required": true
      },
      {
        "key": "channel",
        "label": "Kanal",
        "type": "select",
        "required": false,
        "options": [
          "app",
          "browser_push",
          "email",
          "sms",
          "whatsapp_business"
        ]
      },
      {
        "key": "subject",
        "label": "Betreff",
        "type": "text",
        "required": false
      },
      {
        "key": "body",
        "label": "Text",
        "type": "textarea",
        "required": false
      },
      {
        "key": "variables_json",
        "label": "Variablen JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "lexware_sync_logs",
    "route": "lexware",
    "label": "Lexware Office Sync",
    "icon": "sync_alt",
    "category": "Integrationen",
    "table": "lexware_sync_logs",
    "permission": "integrations",
    "searchFields": [
      "entity_type",
      "status"
    ],
    "fields": [
      {
        "key": "entity_type",
        "label": "Objekt",
        "type": "select",
        "required": false,
        "options": [
          "customer",
          "quote",
          "invoice",
          "payment",
          "project"
        ]
      },
      {
        "key": "entity_id",
        "label": "Objekt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "external_id",
        "label": "Lexware-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "direction",
        "label": "Richtung",
        "type": "select",
        "required": false,
        "options": [
          "push",
          "pull"
        ]
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "queued",
          "success",
          "failed",
          "skipped"
        ]
      },
      {
        "key": "message",
        "label": "Meldung",
        "type": "textarea",
        "required": false
      },
      {
        "key": "payload_json",
        "label": "Payload JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "synced_at",
        "label": "Synchronisiert am",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "workflows",
    "route": "workflows",
    "label": "Workflows",
    "icon": "account_tree",
    "category": "Administration",
    "table": "workflows",
    "permission": "admin",
    "searchFields": [
      "name",
      "entity_type"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Workflow",
        "type": "text",
        "required": true
      },
      {
        "key": "entity_type",
        "label": "Objekt",
        "type": "select",
        "required": false,
        "options": [
          "defect",
          "task",
          "inspection",
          "acceptance",
          "document",
          "project"
        ]
      },
      {
        "key": "definition_json",
        "label": "Definition JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      },
      {
        "key": "notes",
        "label": "Notizen",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "checklist_templates",
    "route": "checklisten-vorlagen",
    "label": "Checklisten-Vorlagen",
    "icon": "checklist",
    "category": "Administration",
    "table": "checklist_templates",
    "permission": "admin",
    "searchFields": [
      "name",
      "template_type"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Checkliste",
        "type": "text",
        "required": true
      },
      {
        "key": "template_type",
        "label": "Typ",
        "type": "select",
        "required": false,
        "options": [
          "Erstbegehung",
          "Tagesbericht",
          "Untergrund",
          "CM-Messung",
          "Abnahme",
          "Mangel"
        ]
      },
      {
        "key": "items_json",
        "label": "Punkte JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "requires_photos",
        "label": "Fotopflicht",
        "type": "boolean",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "users",
    "route": "benutzer",
    "label": "Benutzer",
    "icon": "person",
    "category": "Administration",
    "table": "users",
    "permission": "admin",
    "searchFields": [
      "name",
      "email"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Name",
        "type": "text",
        "required": true
      },
      {
        "key": "email",
        "label": "E-Mail",
        "type": "email",
        "required": true
      },
      {
        "key": "phone",
        "label": "Telefon",
        "type": "phone",
        "required": false
      },
      {
        "key": "is_active",
        "label": "Aktiv",
        "type": "boolean",
        "required": false
      },
      {
        "key": "two_factor_enabled",
        "label": "2FA aktiv",
        "type": "boolean",
        "required": false
      },
      {
        "key": "role_hint",
        "label": "Rollenhinweis",
        "type": "text",
        "required": false
      }
    ]
  },
  {
    "key": "roles",
    "route": "rollen",
    "label": "Rollen",
    "icon": "admin_panel_settings",
    "category": "Administration",
    "table": "roles",
    "permission": "admin",
    "searchFields": [
      "name",
      "code"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Rolle",
        "type": "text",
        "required": true
      },
      {
        "key": "code",
        "label": "Code",
        "type": "text",
        "required": true
      },
      {
        "key": "description",
        "label": "Beschreibung",
        "type": "textarea",
        "required": false
      },
      {
        "key": "is_system",
        "label": "Systemrolle",
        "type": "boolean",
        "required": false
      }
    ]
  },
  {
    "key": "device_sessions",
    "route": "geraete",
    "label": "Geräte & Sessions",
    "icon": "devices",
    "category": "Administration",
    "table": "device_sessions",
    "permission": "security",
    "searchFields": [
      "device_name",
      "status"
    ],
    "fields": [
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "device_name",
        "label": "Gerät",
        "type": "text",
        "required": true
      },
      {
        "key": "device_fingerprint",
        "label": "Fingerprint",
        "type": "text",
        "required": false
      },
      {
        "key": "ip",
        "label": "IP",
        "type": "text",
        "required": false
      },
      {
        "key": "user_agent",
        "label": "User Agent",
        "type": "textarea",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "active",
          "revoked",
          "expired"
        ]
      },
      {
        "key": "last_seen_at",
        "label": "Zuletzt gesehen",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "api_tokens",
    "route": "api-zugriff",
    "label": "API Zugriff",
    "icon": "key",
    "category": "Administration",
    "table": "api_tokens",
    "permission": "security",
    "searchFields": [
      "name",
      "status"
    ],
    "fields": [
      {
        "key": "name",
        "label": "Tokenname",
        "type": "text",
        "required": true
      },
      {
        "key": "token_hash",
        "label": "Token Hash",
        "type": "text",
        "required": false
      },
      {
        "key": "scopes_json",
        "label": "Scopes JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "active",
          "revoked",
          "expired"
        ]
      },
      {
        "key": "expires_at",
        "label": "Gültig bis",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "audit_logs",
    "route": "audit-log",
    "label": "Audit Log",
    "icon": "policy",
    "category": "Administration",
    "table": "audit_logs",
    "permission": "audit",
    "searchFields": [
      "entity_type",
      "action"
    ],
    "fields": [
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "entity_type",
        "label": "Objekt",
        "type": "text",
        "required": false
      },
      {
        "key": "entity_id",
        "label": "Objekt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "action",
        "label": "Aktion",
        "type": "select",
        "required": false,
        "options": [
          "create",
          "update",
          "delete",
          "login",
          "export",
          "share",
          "archive",
          "sync"
        ]
      },
      {
        "key": "before_json",
        "label": "Vorher JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "after_json",
        "label": "Nachher JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "ip",
        "label": "IP",
        "type": "text",
        "required": false
      },
      {
        "key": "user_agent",
        "label": "User Agent",
        "type": "textarea",
        "required": false
      }
    ]
  },
  {
    "key": "offline_sync_jobs",
    "route": "offline-sync",
    "label": "Offline Sync",
    "icon": "cloud_sync",
    "category": "Offline & Betrieb",
    "table": "offline_sync_jobs",
    "permission": "offline",
    "searchFields": [
      "client_id",
      "status"
    ],
    "fields": [
      {
        "key": "client_id",
        "label": "Client-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "operation",
        "label": "Operation",
        "type": "select",
        "required": false,
        "options": [
          "create",
          "update",
          "delete",
          "upload",
          "resolve_conflict"
        ]
      },
      {
        "key": "entity_type",
        "label": "Objekt",
        "type": "text",
        "required": false
      },
      {
        "key": "entity_id",
        "label": "Objekt-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "payload_json",
        "label": "Payload JSON",
        "type": "json",
        "required": false
      },
      {
        "key": "status",
        "label": "Status",
        "type": "select",
        "required": false,
        "options": [
          "queued",
          "processing",
          "success",
          "failed",
          "conflict"
        ]
      },
      {
        "key": "error_message",
        "label": "Fehler",
        "type": "textarea",
        "required": false
      },
      {
        "key": "processed_at",
        "label": "Verarbeitet am",
        "type": "datetime",
        "required": false
      }
    ]
  },
  {
    "key": "login_logs",
    "route": "login-logs",
    "label": "Login Logs",
    "icon": "login",
    "category": "Offline & Betrieb",
    "table": "login_logs",
    "permission": "security",
    "searchFields": [
      "email",
      "success"
    ],
    "fields": [
      {
        "key": "user_id",
        "label": "Benutzer-ID",
        "type": "text",
        "required": false
      },
      {
        "key": "email",
        "label": "E-Mail",
        "type": "email",
        "required": false
      },
      {
        "key": "ip",
        "label": "IP",
        "type": "text",
        "required": false
      },
      {
        "key": "user_agent",
        "label": "User Agent",
        "type": "textarea",
        "required": false
      },
      {
        "key": "success",
        "label": "Erfolgreich",
        "type": "boolean",
        "required": false
      },
      {
        "key": "failure_reason",
        "label": "Fehlergrund",
        "type": "text",
        "required": false
      }
    ]
  }
];

export function moduleByKey(key: string): EnterpriseModule | undefined {
  return ENTERPRISE_MODULES.find((module) => module.key === key);
}
