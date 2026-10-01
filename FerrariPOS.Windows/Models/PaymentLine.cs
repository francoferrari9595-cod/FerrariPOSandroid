namespace FerrarisPOS.Models;
public record PaymentLine(string Method, double Amount, string Reference = "");
