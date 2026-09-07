using CPCREDO.Domain.Loans;
using Xunit;

namespace CPCREDO.Tests.Loans;

public sealed class FlatTermInterestTests
{
    [Fact]
    public void Ten_thousand_at_twenty_percent_for_term_is_two_thousand_interest()
    {
        Assert.Equal(2_000m, FlatTermInterest.TotalInterest(10_000m, 20m));
        Assert.Equal(12_000m, FlatTermInterest.TotalDue(10_000m, 20m));
    }

    [Fact]
    public void Agreed_rate_is_not_annual_and_not_declining()
    {
        var interest = FlatTermInterest.TotalInterest(10_000m, 20m);
        Assert.Equal(2_000m, interest);
        Assert.NotEqual(10_000m * 0.20m * 90m / 365m, interest);
        Assert.NotEqual(10_000m * 0.20m / 12m * 3m, interest);
    }

    [Fact]
    public void Weekly_flat_schedule_is_twelve_payments_of_one_thousand()
    {
        var lines = LoanScheduleFactory.BuildWeeklyFlat(10_000m, 20m, 12, new DateOnly(2026, 1, 5));

        Assert.Equal(12, lines.Count);
        Assert.Equal(12_000m, lines.Sum(l => l.TotalDue));
        Assert.Equal(10_000m, lines.Sum(l => l.PrincipalDue));
        Assert.Equal(2_000m, lines.Sum(l => l.InterestDue));
        Assert.All(lines, line => Assert.Equal(1_000m, line.TotalDue));

        Assert.Equal(833.3333m, lines[0].PrincipalDue);
        Assert.Equal(166.6667m, lines[0].InterestDue);
        Assert.Equal(833.3337m, lines[^1].PrincipalDue);
        Assert.Equal(166.6663m, lines[^1].InterestDue);
        Assert.Equal(new DateOnly(2026, 1, 12), lines[0].DueDate);
        Assert.Equal(new DateOnly(2026, 3, 30), lines[^1].DueDate);
    }

    [Fact]
    public void Payoff_on_day_45_charges_pro_rata_interest_on_a_90_day_term()
    {
        var interest = FlatTermInterest.AccruedInterest(10_000m, 20m, 90, 45, chargesFullFlatInterest: false);
        Assert.Equal(1_000m, interest);
        Assert.Equal(11_000m, 10_000m + interest);
    }

    [Fact]
    public void Payoff_on_day_45_charges_full_flat_interest_when_product_flag_is_on()
    {
        var interest = FlatTermInterest.AccruedInterest(10_000m, 20m, 90, 45, chargesFullFlatInterest: true);
        Assert.Equal(2_000m, interest);
    }
}
