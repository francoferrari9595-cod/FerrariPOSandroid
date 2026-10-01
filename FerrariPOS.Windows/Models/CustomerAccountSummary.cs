namespace FerrarisPOS.Models;

public sealed class CustomerAccountSummary
{
    public Customer Customer { get; }
    public double Balance { get; }
    public double LastCredit { get; }
    public double Available { get; }

    public string Name => Customer.Name;
    public double CreditLimit => Customer.CreditLimit;
    public string BalanceText => $"$ {Balance:N2}";
    public string LastCreditText => $"$ {LastCredit:N2}";
    public string AvailableText => double.IsPositiveInfinity(Available) ? "INFINITO" : $"$ {Available:N2}";

    public CustomerAccountSummary(Customer customer, double balance, double lastCredit, double available)
    {
        Customer = customer;
        Balance = balance;
        LastCredit = lastCredit;
        Available = available;
    }
}
