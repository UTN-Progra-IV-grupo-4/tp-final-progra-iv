-- Datos de ejemplo confirmados para la demo (#12), ampliados por #13.
-- Dos eventos, cada uno con filas A y B de diez asientos (40 asientos en total).
-- Ejecutar manualmente DESPUÉS de aplicar todas las migraciones; no corre al iniciar Web.
-- Los seis IDs originales se conservan. Los restantes usan sufijos 7..40.
-- Los IDs fijos permiten compartir eventos y asientos con Booking.
BEGIN;

INSERT INTO events (id, name, starts_at) VALUES
('10000000-0000-0000-0000-000000000001', 'Conferencia de tecnología', '2030-06-15 18:00:00+00'),
('10000000-0000-0000-0000-000000000002', 'Recital de ejemplo', '2030-06-20 23:00:00+00')
ON CONFLICT (id) DO NOTHING;

WITH demo_events(event_id, event_number) AS (
    VALUES
        ('10000000-0000-0000-0000-000000000001'::uuid, 1),
        ('10000000-0000-0000-0000-000000000002'::uuid, 2)
), generated_seats AS (
    SELECT event_id,
           CASE WHEN position <= 3 THEN (event_number - 1) * 3 + position
                ELSE 6 + (event_number - 1) * 17 + position - 3 END AS seat_id_number,
           CASE WHEN position <= 10 THEN 'A' || position::text
                ELSE 'B' || (position - 10)::text END AS seat_number
    FROM demo_events CROSS JOIN generate_series(1, 20) AS positions(position)
)
INSERT INTO seats (id, event_id, number)
SELECT ('20000000-0000-0000-0000-' || lpad(seat_id_number::text, 12, '0'))::uuid,
       event_id, seat_number
FROM generated_seats
ON CONFLICT (id) DO NOTHING;

COMMIT;
