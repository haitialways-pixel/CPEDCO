using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public static class LoanDelinquency
{
    public static int DaysPastDue(IEnumerable<LoanInstallment> installments, DateOnly asOf)
    {
        var overdue = installments
            .Where(i => LoanRepaymentAllocator.RemainingTotal(i) > 0m && i.DueDate < asOf)
            .Select(i => asOf.DayNumber - i.DueDate.DayNumber)
            .ToList();
        return overdue.Count == 0 ? 0 : overdue.Max();
    }

    public static decimal AccruePenalty(LoanInstallment line, DateOnly asOf, decimal percentPerDay)
    {
        var remaining = MoneyAmount.Normalize(
            LoanRepaymentAllocator.RemainingPrincipal(line) + LoanRepaymentAllocator.RemainingInterest(line));
        if (remaining <= 0m || percentPerDay <= 0m || line.DueDate >= asOf)
            return 0m;
        var from = line.LastPenaltyAccruedOn ?? line.DueDate;
        var days = asOf.DayNumber - from.DayNumber;
        if (days <= 0)
            return 0m;
        return MoneyAmount.Normalize(remaining * (percentPerDay / 100m) * days);
    }
}
