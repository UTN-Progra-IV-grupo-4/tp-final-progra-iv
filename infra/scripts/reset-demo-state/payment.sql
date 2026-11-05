-- Reinicio del estado de demo: base de Payment.
-- Borra los cobros (charges). Payment no guarda catálogo.
-- Ver docs/running-locally.md, sección "Reiniciar el estado entre ensayos".
-- Solo para entornos de demo: no ejecutar contra datos que importen.
BEGIN;

DELETE FROM charges;

COMMIT;
