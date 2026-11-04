using System.ComponentModel.DataAnnotations;

namespace Payment.Business;

// Reglas del caso de uso que conviene ajustar sin recompilar.
public class ChargeOptions
{
    public const string SectionName = "Charge";

    // Un cobro Pending reclamado hace más que esto se considera abandonado (el proceso que lo tenía se
    // cayó) y otro pedido puede reclamarlo. Tiene que superar con margen la demora máxima de la pasarela
    // (SlowDelaySeconds en la simulada): si no, se reclama un cobro que todavía está en curso.
    [Range(1, 3600)]
    public int PendingTimeoutSeconds { get; set; } = 60;

    public TimeSpan PendingTimeout => TimeSpan.FromSeconds(PendingTimeoutSeconds);

    // Monedas aceptadas (#22: solo ARS), en appsettings.json. Sin valor inicial a propósito: el binder
    // de configuración agrega a la colección existente en vez de reemplazarla, así que un ["ARS"] acá
    // más un ["USD"] configurado terminaría aceptando las dos. MinLength frena el arranque si falta.
    [MinLength(1)]
    public string[] SupportedCurrencies { get; set; } = [];

    // Tope por cobro, en centavos: sin él se aprobaba long.MaxValue. Por defecto un millón de pesos,
    // muy por encima del precio de un asiento; un monto mayor es un error de configuración de Booking.
    [Range(typeof(long), "1", "9223372036854775807")]
    public long MaxAmountCents { get; set; } = 100_000_000;
}
