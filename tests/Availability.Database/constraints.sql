-- Pruebas de integración de las restricciones reales en PostgreSQL.
-- Ejecutar después de aplicar todas las migraciones, con el usuario de Availability.
-- Todo se revierte: no elimina ni modifica los datos que ya existían.
\set ON_ERROR_STOP on
BEGIN;

DO $$
DECLARE
    v_event_id uuid := gen_random_uuid();
    v_other_event_id uuid := gen_random_uuid();
    v_seat_id uuid := gen_random_uuid();
    v_other_seat_id uuid := gen_random_uuid();
    request_id uuid := gen_random_uuid();
    missing_id uuid := gen_random_uuid();
    request_time timestamptz := '2030-06-15 18:00:00+00';
BEGIN
    INSERT INTO events (id, name, starts_at) VALUES
        (v_event_id, 'Prueba transaccional', request_time),
        (v_other_event_id, 'Otro evento de prueba', request_time);
    INSERT INTO seats (id, event_id, number) VALUES
        (v_seat_id, v_event_id, 'TEST-A1'),
        (v_other_seat_id, v_other_event_id, 'TEST-A1');
    RAISE NOTICE 'OK: el mismo número de asiento se permite en eventos diferentes';

    BEGIN
        INSERT INTO seats (id, event_id, number) VALUES
            (gen_random_uuid(), v_event_id, 'TEST-A1');
        RAISE EXCEPTION 'FALLO: se permitió un número duplicado en el mismo evento';
    EXCEPTION WHEN unique_violation THEN
        RAISE NOTICE 'OK: número duplicado en el mismo evento rechazado';
    END;

    BEGIN
        INSERT INTO seats (id, event_id, number) VALUES
            (gen_random_uuid(), missing_id, 'TEST-X');
        RAISE EXCEPTION 'FALLO: se permitió un asiento sin evento';
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'OK: asiento con evento inexistente rechazado';
    END;

    BEGIN
        INSERT INTO reservation_requests (id, seat_id, user_id, requested_at) VALUES
            (gen_random_uuid(), missing_id, 'usuario-prueba', request_time);
        RAISE EXCEPTION 'FALLO: se permitió una solicitud sin asiento';
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'OK: solicitud con asiento inexistente rechazada';
    END;

    INSERT INTO reservation_requests (id, seat_id, user_id, requested_at) VALUES
        (request_id, v_seat_id, 'usuario-prueba-1', request_time),
        (gen_random_uuid(), v_seat_id, 'usuario-prueba-2', request_time);
    IF (SELECT count(*) FROM reservation_requests WHERE seat_id = v_seat_id) <> 2 THEN
        RAISE EXCEPTION 'FALLO: no se guardaron dos solicitudes para el mismo asiento';
    END IF;
    RAISE NOTICE 'OK: dos solicitudes distintas para el mismo asiento son válidas';

    BEGIN
        INSERT INTO reservation_requests (id, seat_id, user_id, requested_at) VALUES
            (request_id, v_seat_id, 'usuario-prueba-3', request_time);
        RAISE EXCEPTION 'FALLO: se permitió repetir el identificador de solicitud';
    EXCEPTION WHEN unique_violation THEN
        RAISE NOTICE 'OK: identificador de solicitud duplicado rechazado';
    END;

    BEGIN
        DELETE FROM seats WHERE id = v_seat_id;
        RAISE EXCEPTION 'FALLO: se permitió borrar un asiento con solicitudes';
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'OK: se conserva el asiento con historial de solicitudes';
    END;

    BEGIN
        DELETE FROM events WHERE id = v_event_id;
        RAISE EXCEPTION 'FALLO: se permitió borrar un evento con asientos';
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'OK: se conserva el evento con asientos';
    END;

    IF (SELECT requested_at FROM reservation_requests WHERE id = request_id) <> request_time THEN
        RAISE EXCEPTION 'FALLO: el instante guardado no coincide';
    END IF;
    RAISE NOTICE 'OK: PostgreSQL conserva el instante de la solicitud';
END $$;

ROLLBACK;
\echo Pruebas finalizadas; los datos de prueba se revirtieron.
