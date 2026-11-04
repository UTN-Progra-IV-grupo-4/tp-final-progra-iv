namespace Payment.Data.Entities;

// Estado persistido de un cobro. Es distinto del enum del contrato gRPC: lo que se guarda
// no debe cambiar (ni pedir una migración) cada vez que cambia lo que viaja por el cable.
// Arranca en 1 para que un estado sin asignar (default = 0) nunca pase por uno válido.
public enum ChargeStatus
{
    // El cobro está reservado pero la pasarela todavía no respondió. Se guarda antes de cobrar
    // para que el índice único sobre ReservationId frene los reintentos simultáneos.
    Pending = 1,
    Approved = 2,
    Declined = 3,

    // Error técnico de la pasarela, distinto de un rechazo de negocio: no se cobró nada,
    // así que un reintento puede volver a intentarlo (ver IChargeRepository.TryReclaimAsync).
    Failed = 4,
}
