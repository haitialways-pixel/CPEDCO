using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public sealed record InstallmentAllocation(
    int LineNo,
    decimal Penalty,
    decimal Interest,
    decimal Principal)
{
    public decimal Total => MoneyAmount.Normalize(Penalty + Interest + Principal);
}

public static class LoanRepaymentAllocator
{
    public static decimal RemainingPenalty(LoanInstallment line) =>
        Remain(line.PenaltyDue, line.PenaltyPaid);

    public static decimal RemainingInterest(LoanInstallment line) =>
        Remain(line.InterestDue, line.InterestPaid);

    public static decimal RemainingPrincipal(LoanInstallment line) =>
        Remain(line.PrincipalDue, line.PrincipalPaid);

    public static decimal RemainingTotal(LoanInstallment line) =>
        MoneyAmount.Normalize(RemainingPenalty(line) + RemainingInterest(line) + RemainingPrincipal(line));

    public static IReadOnlyList<InstallmentAllocation> Allocate(
        IEnumerable<LoanInstallment> installments,
        decimal amount)
    {
        var left = MoneyAmount.Normalize(amount);
        var result = new List<InstallmentAllocation>();
        foreach (var line in installments.OrderBy(i => i.LineNo))
        {
            if (left <= 0m)
                break;
            var penalty = Take(ref left, RemainingPenalty(line));
            var interest = Take(ref left, RemainingInterest(line));
            var principal = Take(ref left, RemainingPrincipal(line));
            if (penalty + interest + principal > 0m)
                result.Add(new InstallmentAllocation(line.LineNo, penalty, interest, principal));
        }

        return result;
    }

    public static decimal Unallocated(IEnumerable<LoanInstallment> installments, decimal amount) =>
        MoneyAmount.Normalize(amount - Allocate(installments, amount).Sum(a => a.Total));

    public static void Apply(IEnumerable<LoanInstallment> installments, IEnumerable<InstallmentAllocation> allocations)
    {
        var byLine = installments.ToDictionary(i => i.LineNo);
        foreach (var allocation in allocations)
        {
            if (!byLine.TryGetValue(allocation.LineNo, out var line))
                continue;
            line.PenaltyPaid = MoneyAmount.Normalize(line.PenaltyPaid + allocation.Penalty);
            line.InterestPaid = MoneyAmount.Normalize(line.InterestPaid + allocation.Interest);
            line.PrincipalPaid = MoneyAmount.Normalize(line.PrincipalPaid + allocation.Principal);
        }
    }

    private static decimal Remain(decimal due, decimal paid) =>
        MoneyAmount.Normalize(Math.Max(0m, due - paid));

    private static decimal Take(ref decimal left, decimal due)
    {
        var take = MoneyAmount.Normalize(Math.Min(left, due));
        left = MoneyAmount.Normalize(left - take);
        return take;
    }
}
