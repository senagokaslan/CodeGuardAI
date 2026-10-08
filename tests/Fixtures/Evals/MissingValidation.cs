namespace EvalFixtures;

public sealed class Wallet
{
    public decimal Balance { get; private set; }

    public void Deposit(decimal amount)
    {
        Balance += amount;
    }
}
