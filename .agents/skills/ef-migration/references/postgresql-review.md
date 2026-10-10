# PostgreSQL migration review

Check schema/table qualification, column type, precision and scale, null/default transitions, index/constraint naming, implicit casts, table rewrites, lock duration, existing-data backfill, transactional behavior, and downgrade feasibility. Generate SQL with the repository's configured context and record only safe endpoint names—not connection values.
