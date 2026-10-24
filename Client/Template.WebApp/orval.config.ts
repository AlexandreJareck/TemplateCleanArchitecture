import { defineConfig } from 'orval';

const generatedRoot = process.env['ORVAL_OUTPUT'] ?? 'src/app/infrastructure/api/generated';

export default defineConfig({
  templateApi: {
    input: {
      target: '../../docs/refactoring/baseline-phase-31/openapi-v2.json',
    },
    output: {
      target: `${generatedRoot}/services.ts`,
      schemas: `${generatedRoot}/models`,
      client: 'angular',
      mode: 'tags-split',
      clean: true,
      prettier: true,
      override: {
        angular: {
          retrievalClient: 'httpClient',
        },
      },
    },
  },
});
