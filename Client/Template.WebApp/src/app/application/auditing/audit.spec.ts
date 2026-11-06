import { AuditUseCases } from './audit';

describe('AuditUseCases', () => {
  it('accepts empty filters and a well-formed actor ID', () => {
    expect(AuditUseCases.validate({})).toEqual({});
    expect(AuditUseCases.validate({ actorId: 'd0000000-0000-0000-0000-000000000002' })).toEqual({});
  });

  it('rejects a malformed actor ID and an inverted date range', () => {
    expect(AuditUseCases.validate({ actorId: 'demo.admin' })).toEqual({ actorId: 'actorFormat' });
    expect(AuditUseCases.validate({ fromDate: '2026-11-05', toDate: '2026-11-01' })).toEqual({
      toDate: 'range',
    });
  });

  it('drops empty filters and trims text', () => {
    expect(
      AuditUseCases.compact({
        page: 1,
        pageSize: 20,
        action: '  requisition.submitted ',
        actorId: '',
      }),
    ).toEqual({ page: 1, pageSize: 20, action: 'requisition.submitted' });
  });
});
