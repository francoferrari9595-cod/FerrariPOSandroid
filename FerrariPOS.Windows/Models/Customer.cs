namespace FerrarisPOS.Models;
public record Customer(int Id, string Name, string Document, string Phone, string Email, string Address, double CreditLimit, bool Active);
