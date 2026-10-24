using System.Globalization;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Domain;

public class BillingException : Exception
{
    public string Code { get; }
    public BillingException(string message, string code) : base(message) => Code = code;
}

public static class BillingLogic
{
    public const int MinBalanceForPlanSantim = 50000; // 500.00 ETB
    public const int MinBalanceForPlanCents = MinBalanceForPlanSantim;
    public const int MinPayments = 2;
    public const int MaxPayments = 12;

    public static int ParseMoneyToCents(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new BillingException("Enter a payment amount.", "invalid_amount");

        var cleaned = input.Trim()
            .Replace("ETB", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Br", "", StringComparison.OrdinalIgnoreCase)
            .Replace("ብር", "")
            .TrimStart('$')
            .Trim();

        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var units))
            throw new BillingException("Enter a valid payment amount.", "invalid_amount");

        if (units <= 0)
            throw new BillingException("Payment amount must be greater than zero.", "invalid_amount");

        var santim = (int)Math.Round(units * 100m, MidpointRounding.AwayFromZero);
        return santim;
    }

    public static void AssertPlanAllowed(int balanceCents, int numberOfPayments, bool existingActivePlan, int? minBalanceSantim = null, string? locale = "en-ET")
    {
        if (existingActivePlan)
            throw new BillingException("An active payment plan already exists for this statement.", "already_active");

        var minRequired = minBalanceSantim ?? MinBalanceForPlanSantim;
        if (balanceCents < minRequired)
        {
            var formattedMin = CurrencyFormatter.Format(minRequired, locale);
            throw new BillingException($"Payment plans require an outstanding balance of at least {formattedMin}.", "balance_too_low");
        }

        if (numberOfPayments < MinPayments || numberOfPayments > MaxPayments)
            throw new BillingException($"Number of payments must be between {MinPayments} and {MaxPayments}.", "invalid_installments");
    }

    public record InstallmentScheduleItem(int Sequence, int AmountCents, DateTime DueAt);

    public static List<InstallmentScheduleItem> BuildSchedule(int totalCents, int numberOfPayments, DateTime startDate)
    {
        var baseAmount = totalCents / numberOfPayments;
        var remainder = totalCents % numberOfPayments;

        var schedule = new List<InstallmentScheduleItem>();

        for (int i = 1; i <= numberOfPayments; i++)
        {
            var amount = (i == 1) ? baseAmount + remainder : baseAmount;
            var dueDate = startDate.AddMonths(i - 1);
            schedule.Add(new InstallmentScheduleItem(i, amount, dueDate));
        }

        return schedule;
    }

    public record FifoAllocationResult(Guid StatementId, int AllocatedCents, int RemainingStatementBalanceCents, StatementStatus NewStatus);

    public static List<FifoAllocationResult> AllocatePaymentFifo(
        IEnumerable<Statement> openStatements,
        int amountCents)
    {
        if (amountCents <= 0)
            throw new BillingException("Payment amount must be greater than zero.", "invalid_amount");

        var sorted = openStatements
            .Where(s => s.BalanceCents > 0 && s.Status != StatementStatus.PAID && s.Status != StatementStatus.VOID)
            .OrderBy(s => s.DueAt ?? s.CreatedAt)
            .ToList();

        var remaining = amountCents;
        var allocations = new List<FifoAllocationResult>();

        foreach (var st in sorted)
        {
            if (remaining <= 0) break;

            var payForThis = Math.Min(st.BalanceCents, remaining);
            st.AmountPaidCents += payForThis;
            st.BalanceCents -= payForThis;

            if (st.BalanceCents == 0)
            {
                st.Status = StatementStatus.PAID;
            }
            else
            {
                st.Status = StatementStatus.PARTIALLY_PAID;
            }

            remaining -= payForThis;
            allocations.Add(new FifoAllocationResult(st.Id, payForThis, st.BalanceCents, st.Status));
        }

        return allocations;
    }

    public static void RevertAllocation(Statement statement, int refundCents)
    {
        statement.AmountPaidCents = Math.Max(0, statement.AmountPaidCents - refundCents);
        statement.BalanceCents = Math.Min(statement.TotalCents, statement.BalanceCents + refundCents);

        if (statement.AmountPaidCents == 0)
        {
            statement.Status = StatementStatus.OPEN;
        }
        else if (statement.BalanceCents == 0)
        {
            statement.Status = StatementStatus.PAID;
        }
        else
        {
            statement.Status = StatementStatus.PARTIALLY_PAID;
        }
    }
}
