namespace EvalFixtures;

public sealed class CustomerFormatter
{
    public int GetNameLength(Customer customer)
    {
        return customer.Name.Length;
    }
}

public sealed record Customer(string Name);
