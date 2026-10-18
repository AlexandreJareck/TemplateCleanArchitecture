#nullable enable
using Template.Domain.Auditing;

namespace Template.Application.Interfaces.Auditing;

/// <summary>
/// Append-only audit port (AUD-001, AUD-002). It deliberately has no update or delete member; the record is written
/// by the same <see cref="IUnitOfWork"/> commit as the change it describes.
/// </summary>
public interface IAuditTrail
{
    void Append(AuditRecord record);
}
