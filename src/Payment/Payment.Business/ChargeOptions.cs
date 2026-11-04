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
}
