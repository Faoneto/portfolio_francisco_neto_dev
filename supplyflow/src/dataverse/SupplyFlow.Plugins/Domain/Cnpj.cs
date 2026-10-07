using System.Text;

namespace SupplyFlow.Plugins.Domain;

/// <summary>
/// CNPJ normalization and validation, supporting both the classic numeric format and the
/// alphanumeric format introduced by Receita Federal in July/2026 (IN RFB nº 2.229/2024).
/// </summary>
/// <remarks>
/// Alphanumeric rule: the first 12 positions accept [0-9A-Z], the last 2 (check digits) stay numeric.
/// Each character is converted using (ASCII code − 48), so '0'..'9' keep their value and 'A' = 17 … 'Z' = 42.
/// The module-11 algorithm and weights are unchanged, which keeps every numeric CNPJ valid.
/// </remarks>
public static class Cnpj
{
    public const int Length = 14;

    private static readonly int[] FirstDigitWeights = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
    private static readonly int[] SecondDigitWeights = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

    /// <summary>Removes the mask (".", "/", "-", spaces) and upper-cases the value.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Length);
        foreach (var c in value!)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }

    public static bool IsValid(string? value)
    {
        var cnpj = Normalize(value);
        if (cnpj.Length != Length)
        {
            return false;
        }

        for (var i = 0; i < 12; i++)
        {
            if (!IsAllowedBaseChar(cnpj[i]))
            {
                return false;
            }
        }

        if (!char.IsDigit(cnpj[12]) || !char.IsDigit(cnpj[13]))
        {
            return false;
        }

        // Sequences such as 00000000000000 pass module 11 but are not valid registrations.
        if (cnpj.Trim(cnpj[0]).Length == 0)
        {
            return false;
        }

        var first = CalculateDigit(cnpj, FirstDigitWeights);
        var second = CalculateDigit(cnpj, SecondDigitWeights);
        return cnpj[12] - '0' == first && cnpj[13] - '0' == second;
    }

    /// <summary>Formats a valid CNPJ as XX.XXX.XXX/XXXX-XX.</summary>
    public static string Format(string? value)
    {
        var cnpj = Normalize(value);
        if (cnpj.Length != Length)
        {
            return value ?? string.Empty;
        }

        return $"{cnpj.Substring(0, 2)}.{cnpj.Substring(2, 3)}.{cnpj.Substring(5, 3)}/{cnpj.Substring(8, 4)}-{cnpj.Substring(12, 2)}";
    }

    private static bool IsAllowedBaseChar(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'Z';

    private static int CalculateDigit(string cnpj, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            sum += (cnpj[i] - 48) * weights[i];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
