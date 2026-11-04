namespace Availability.Tests;

// Las futuras clases de integración deben usar esta colección para reutilizar
// el contenedor y evitar limpiezas concurrentes sobre la misma base de tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "Availability PostgreSQL integration";
}
