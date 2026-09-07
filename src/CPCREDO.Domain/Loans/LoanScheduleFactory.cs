using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public sealed record LoanInstallmentPlan(
    int LineNo,
    DateOnly DueDate,
    decimal PrincipalDue,
    decimal InterestDue,
    decimal TotalDue);

public static class LoanScheduleFactory
{
    public static IReadOnlyList<LoanInstallmentPlan> BuildWeeklyFlat(
        decimal principal,
        decimal agreedRatePercent,
        int installmentCount,
        DateOnly startDate)
    {
        if (installmentCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(installmentCount));
        if (principal <= 0m)
            throw new ArgumentOutOfRangeException(nameof(principal));

        var n = installmentCount;
        var totalInterest = FlatTermInterest.TotalInterest(principal, agreedRatePercent);
        var principalEach = MoneyAmount.Normalize(principal / n);
        var interestEach = MoneyAmount.Normalize(totalInterest / n);

        var lines = new List<LoanInstallmentPlan>(n);
        var principalAllocated = 0m;
        var interestAllocated = 0m;
        for (var i = 1; i <= n; i++)
        {
            decimal principalDue;
            decimal interestDue;
            if (i == n)
            {
                principalDue = MoneyAmount.Normalize(principal - principalAllocated);
                interestDue = MoneyAmount.Normalize(totalInterest - interestAllocated);
            }
            else
            {
                principalDue = principalEach;
                interestDue = interestEach;
            }

            principalAllocated += principalDue;
            interestAllocated += interestDue;
            lines.Add(new LoanInstallmentPlan(
                i,
                startDate.AddDays(7 * i),
                principalDue,
                interestDue,
                MoneyAmount.Normalize(principalDue + interestDue)));
        }

        return lines;
    }
}
