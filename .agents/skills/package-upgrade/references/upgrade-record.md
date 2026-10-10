# Upgrade record

Record: consultation date and timezone; official release/package URLs; current and selected stable versions; target framework compatibility; advisories and parent dependency paths; breaking changes; rollback; restore/audit/build/test results.

Prefer upgrading the owning direct package. Use a transitive override only when the parent cannot select a safe compatible version and official compatibility plus tests support the override. Keep Microsoft/ASP.NET/EF patch versions aligned and Npgsql on its compatible EF line.
