using System.Data.Common;

namespace Payment.Tests.TestDoubles;

// Falla de base transitoria sin depender de Npgsql: la presentación solo mira DbException.IsTransient.
public sealed class TestTransientDbException(bool isTransient = true) : DbException("falla simulada de base")
{
    public override bool IsTransient => isTransient;
}
