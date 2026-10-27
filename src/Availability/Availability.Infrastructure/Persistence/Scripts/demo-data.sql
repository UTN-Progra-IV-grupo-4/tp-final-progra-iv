-- Propuesta de datos de ejemplo para desarrollo, a confirmar con el equipo (#12).
-- Ejecutar manualmente sobre la base de Availability DESPUÉS de aplicar la migración.
-- No se ejecuta al arrancar Web ni carga solicitudes de usuarios ficticios.
-- IDs fijos permiten que Booking use los mismos eventos/asientos en una demo.
BEGIN;

INSERT INTO "Events" ("Id", "Name", "StartsAt") VALUES
('10000000-0000-0000-0000-000000000001', 'Conferencia de tecnología', '2030-06-15 18:00:00+00'),
('10000000-0000-0000-0000-000000000002', 'Recital de ejemplo', '2030-06-20 23:00:00+00')
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "Seats" ("Id", "EventId", "Number") VALUES
('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'A1'),
('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'A2'),
('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 'A3'),
('20000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000002', 'A1'),
('20000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000002', 'A2'),
('20000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000002', 'A3')
ON CONFLICT ("Id") DO NOTHING;

COMMIT;
