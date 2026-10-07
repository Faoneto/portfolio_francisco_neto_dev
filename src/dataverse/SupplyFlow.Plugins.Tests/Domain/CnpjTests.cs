using SupplyFlow.Plugins.Domain;
using Xunit;

namespace SupplyFlow.Plugins.Tests.Domain;

public class CnpjTests
{
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    [InlineData(" 11 222 333 0001 81 ")]
    [InlineData("12ABC34501DE35")] // official alphanumeric example (Receita Federal, 2026)
    [InlineData("12.ABC.345/01DE-35")]
    [InlineData("12.abc.345/01de-35")]
    public void Valid_cnpjs_are_accepted(string value)
    {
        Assert.True(Cnpj.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("11222333000182")] // wrong check digit
    [InlineData("1122233300018")] // 13 chars
    [InlineData("112223330001811")] // 15 chars
    [InlineData("00000000000000")] // repeated sequence
    [InlineData("AAAAAAAAAAAA00")] // repeated sequence (alphanumeric)
    [InlineData("12ABC34501DE3A")] // check digits must be numeric
    [InlineData("12ABC34501DE36")]
    public void Invalid_cnpjs_are_rejected(string? value)
    {
        Assert.False(Cnpj.IsValid(value));
    }

    [Fact]
    public void Normalize_removes_mask_and_upper_cases()
    {
        Assert.Equal("12ABC34501DE35", Cnpj.Normalize("12.abc.345/01de-35"));
    }

    [Theory]
    [InlineData("11222333000181", "11.222.333/0001-81")]
    [InlineData("12abc34501de35", "12.ABC.345/01DE-35")]
    public void Format_applies_mask(string raw, string expected)
    {
        Assert.Equal(expected, Cnpj.Format(raw));
    }
}
