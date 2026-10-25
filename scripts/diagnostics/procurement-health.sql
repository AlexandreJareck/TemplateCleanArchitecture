-- Phase 33 read-only diagnostics of the application database (run by scripts/procurement-diagnostics.ps1).
-- Counts and codes only: no payloads, names, amounts or correlation values are selected.
SET default_transaction_read_only = on;
\echo '-- outbox backlog'
SELECT count(*) FILTER (WHERE "DispatchedAtUtc" IS NULL AND "PoisonedAtUtc" IS NULL) AS pending,
       count(*) FILTER (WHERE "PoisonedAtUtc" IS NOT NULL) AS poisoned,
       EXTRACT(EPOCH FROM now() at time zone 'utc' - min("CreatedAtUtc") FILTER (WHERE "DispatchedAtUtc" IS NULL AND "PoisonedAtUtc" IS NULL))::int AS oldest_pending_seconds
FROM "MessagingOutbox";
\echo '-- outbox failures by code'
SELECT "EventType", "LastFailureCode", count(*) FROM "MessagingOutbox" WHERE "LastFailureCode" IS NOT NULL GROUP BY 1,2 ORDER BY 3 DESC LIMIT 20;
\echo '-- inbox in progress (received, not completed) per consumer'
SELECT "ConsumerName", count(*) FILTER (WHERE "CompletedAtUtc" IS NULL) AS incomplete, count(*) AS total FROM "MessagingInbox" GROUP BY 1;
\echo '-- report jobs by status'
SELECT "Status", count(*), max("AttemptCount") AS max_attempts FROM "ReportingReportJobs" GROUP BY 1;
\echo '-- report failures'
SELECT "FailureCode", count(*) FROM "ReportingReportJobs" WHERE "FailureCode" IS NOT NULL GROUP BY 1;
\echo '-- approval instances by status'
SELECT "Status", count(*) FROM "ApprovalInstances" GROUP BY 1;
\echo '-- notification deliveries by status'
SELECT "Status", "Template", count(*) FROM "MessagingNotificationDeliveries" GROUP BY 1,2 ORDER BY 1,2;
