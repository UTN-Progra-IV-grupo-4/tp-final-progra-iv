-- Reinicio del estado de demo: base de Availability.
-- Borra las solicitudes de reserva (reservation_requests) y CONSERVA el catálogo
-- (events y seats), es decir, lo que cargó demo-data.sql.
-- Ver docs/running-locally.md, sección "Reiniciar el estado entre ensayos".
-- Solo para entornos de demo: no ejecutar contra datos que importen.
BEGIN;

DELETE FROM reservation_requests;

COMMIT;
