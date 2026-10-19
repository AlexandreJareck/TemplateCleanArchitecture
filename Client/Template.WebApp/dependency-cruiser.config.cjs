module.exports = {
  forbidden: [
    {
      name: 'domain-is-framework-free',
      severity: 'error',
      from: { path: '^src/app/domain' },
      to: {
        path: '^(src/app/(application|infrastructure|presentation|bootstrap)|node_modules/(@angular|rxjs))',
      },
    },
    {
      name: 'application-does-not-depend-outward',
      severity: 'error',
      from: { path: '^src/app/application' },
      to: {
        path: '^(src/app/(infrastructure|presentation|bootstrap)|node_modules/(@angular|rxjs))',
      },
    },
    {
      name: 'infrastructure-does-not-depend-on-ui-or-bootstrap',
      severity: 'error',
      from: { path: '^src/app/infrastructure' },
      to: { path: '^src/app/(presentation|bootstrap)' },
    },
    {
      name: 'presentation-does-not-depend-on-infrastructure-or-bootstrap',
      severity: 'error',
      from: { path: '^src/app/presentation' },
      to: { path: '^src/app/(infrastructure|bootstrap)' },
    },
    {
      name: 'generated-client-does-not-leak-inward',
      severity: 'error',
      from: { path: '^src/app/(domain|application|presentation)' },
      to: { path: '^src/app/infrastructure/api/generated' },
    },
    {
      name: 'bootstrap-is-not-imported-by-layers',
      severity: 'error',
      from: { path: '^src/app/(domain|application|infrastructure|presentation)' },
      to: { path: '^src/app/bootstrap' },
    },
  ],
  options: {
    doNotFollow: { path: 'node_modules|src/app/infrastructure/api/generated/' },
    exclude: '(^|/)node_modules/',
    tsConfig: { fileName: 'tsconfig.app.json' },
    enhancedResolveOptions: {
      exportsFields: ['exports'],
      conditionNames: ['import', 'types', 'default'],
    },
  },
};
