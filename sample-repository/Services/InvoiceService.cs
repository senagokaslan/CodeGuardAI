namespace CodeGuard.Sample.Services;

public sealed class InvoiceService
{
    public decimal CalculateTotal(IReadOnlyList<decimal>? lineItems)
    {
        return lineItems!.Sum();
    }

    public decimal ApplyDiscount(decimal total, decimal percentage)
    {
        return total - (total * percentage / 100m);
    }
}
