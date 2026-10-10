START TRANSACTION;
ALTER TABLE "Products" ALTER COLUMN "Price" TYPE numeric(18,2);

ALTER TABLE "Products" ALTER COLUMN "Name" TYPE character varying(100);
UPDATE "Products" SET "Name" = '' WHERE "Name" IS NULL;
ALTER TABLE "Products" ALTER COLUMN "Name" SET NOT NULL;
ALTER TABLE "Products" ALTER COLUMN "Name" SET DEFAULT '';

ALTER TABLE "Products" ALTER COLUMN "BarCode" TYPE character varying(50);
UPDATE "Products" SET "BarCode" = '' WHERE "BarCode" IS NULL;
ALTER TABLE "Products" ALTER COLUMN "BarCode" SET NOT NULL;
ALTER TABLE "Products" ALTER COLUMN "BarCode" SET DEFAULT '';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261010151729_ProductPriceDecimal', '10.0.12');

COMMIT;

