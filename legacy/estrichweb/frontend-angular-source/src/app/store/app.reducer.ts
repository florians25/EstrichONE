import { createReducer, on, createAction, props } from '@ngrx/store';

export interface AppState {
  tenantName: string;
  darkMode: boolean;
  offlineQueueLength: number;
}

export const setTenantName = createAction('[App] Set Tenant Name', props<{ tenantName: string }>());
export const setOfflineQueueLength = createAction('[App] Set Offline Queue Length', props<{ offlineQueueLength: number }>());
export const toggleDarkMode = createAction('[App] Toggle Dark Mode');

export const initialState: AppState = { tenantName: 'EstrichWeb', darkMode: true, offlineQueueLength: 0 };

export const appReducer = createReducer(
  initialState,
  on(setTenantName, (state, action) => ({ ...state, tenantName: action.tenantName })),
  on(setOfflineQueueLength, (state, action) => ({ ...state, offlineQueueLength: action.offlineQueueLength })),
  on(toggleDarkMode, (state) => ({ ...state, darkMode: !state.darkMode }))
);
