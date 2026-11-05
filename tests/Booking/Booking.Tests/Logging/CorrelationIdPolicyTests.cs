using Contracts;

namespace Booking.Tests.Logging;

public sealed class CorrelationIdPolicyTests
{
    [Theory]
    [InlineData("3f2b8d1e-0000-4000-8000-000000000024")]
    [InlineData("abc-123_X.y:z")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")] // 64 characters, the maximum
    public void Sanitize_WithSafeValue_ReturnsItUnchanged(string value) =>
        Assert.Equal(value, CorrelationIdPolicy.Sanitize(value));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Sanitize_WithoutValue_ReturnsNull(string? value) =>
        Assert.Null(CorrelationIdPolicy.Sanitize(value));

    [Theory]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 characters
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("forged\r\nwarn: Booking fake entry")]
    [InlineData("tab\tseparated")]
    [InlineData("null\0byte")]
    [InlineData("with space")]
    [InlineData("<script>")]
    [InlineData("first,second")]
    [InlineData("\u0663\u0662")] // Arabic-Indic digits are digits for char.IsDigit but not safe ASCII
    public void Sanitize_WithUnsafeValue_ReturnsThePlaceholderInsteadOfTheValue(string value) =>
        Assert.Equal(CorrelationIdPolicy.InvalidValue, CorrelationIdPolicy.Sanitize(value));
}
