namespace CepApi.Infrastructure.Services;

public sealed class SecurityCodeOptions
{
    public const string SectionName = "SecurityCodes";
    public string HmacKey { get; set; } = string.Empty;

    public byte[] ReadHmacKey()
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(HmacKey?.Trim() ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("SecurityCodes:HmacKey must be valid Base64.", exception);
        }

        if (key.Length < 32)
            throw new InvalidOperationException("SecurityCodes:HmacKey must contain at least 32 random bytes.");
        return key;
    }

}
