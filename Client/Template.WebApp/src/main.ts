import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { createAppConfig } from './app/bootstrap/app-providers';
import { loadRuntimeConfig } from './app/infrastructure/runtime/runtime-config';

loadRuntimeConfig()
  .then((runtimeConfig) => bootstrapApplication(App, createAppConfig(runtimeConfig)))
  .catch((error: unknown) => console.error('Application bootstrap failed.', error));
