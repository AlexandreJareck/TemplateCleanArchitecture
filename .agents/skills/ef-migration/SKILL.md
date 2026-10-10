---
name: ef-migration
description: Create and validate an additive Entity Framework Core migration when an entity mapping or relational schema change requires one. Do not activate when no schema delta exists.
---

# EF migration

Inputs are the DbContext, intended schema delta, target PostgreSQL version, and rollout constraints. Read [references/postgresql-review.md](references/postgresql-review.md).

1. Prove a real model/schema delta and identify the last applied migration.
2. Generate a new migration with EF tooling; never hand-edit designer or snapshot files.
3. Review generated operations and idempotent PostgreSQL SQL, including data conversion and locks.
4. Validate forward migration, rollback/restore strategy, and provider-sensitive integration tests.

Stop on destructive or ambiguous data conversion, missing backup/rollback authority, or tooling/model mismatch. Never rewrite an applied migration, connect to an unapproved database, or reveal connection-string values. Output migration files, SQL-review findings, validation, and rollout notes.
