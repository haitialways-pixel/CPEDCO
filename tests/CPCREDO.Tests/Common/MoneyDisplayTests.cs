using CPCREDO.Domain.Common;
using Xunit;

namespace CPCREDO.Tests.Common;

public sealed class MoneyDisplayTests
{
    [Fact]
    public void Formats_htg_with_thin_space_and_two_decimals()
    {
        Assert.Equal($"1{MoneyDisplay.ThinSpace}000,25 G", MoneyDisplay.Format(1000.25m, Currencies.Htg));
    }

    [Fact]
    public void Formats_usd_with_dollar_suffix()
    {
        Assert.Equal("12,50 $US", MoneyDisplay.Format(12.5m, Currencies.Usd));
    }

    [Fact]
    public void Display_rounds_half_away_from_zero_without_changing_storage_scale()
    {
        Assert.Equal(1.2346m, MoneyAmount.Normalize(1.23456m));
        Assert.Equal("1,23 G", MoneyDisplay.Format(1.2346m, Currencies.Htg));
        Assert.Equal("1,24 G", MoneyDisplay.Format(1.2356m, Currencies.Htg));
    }

    [Fact]
    public void Zero_and_negatives()
    {
        Assert.Equal("0,00 G", MoneyDisplay.Format(0m, Currencies.Htg));
        Assert.Equal($"-1{MoneyDisplay.ThinSpace}000,25 G", MoneyDisplay.Format(-1000.25m, Currencies.Htg));
    }
}
