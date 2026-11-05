-- Reinicio del estado de demo: base de Booking.
-- Borra todas las reservas (retenidas, confirmadas, rechazadas y vencidas), que es
-- lo que mantiene ocupados los asientos. Booking no guarda catálogo.
-- Ver docs/running-locally.md, sección "Reiniciar el estado entre ensayos".
-- Solo para entornos de demo: no ejecutar contra datos que importen.
BEGIN;

DELETE FROM reservations;

COMMIT;
