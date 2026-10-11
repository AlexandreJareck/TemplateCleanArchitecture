namespace Template.Infrastructure.Identity.Settings;

/// <summary>Password policy bound from the <c>IdentitySettings</c> section and validated at startup.</summary>
public sealed class IdentitySettings
{
    public const string SectionName = nameof(IdentitySettings);
    public const int MinimumPasswordLength = 4;
    public const int MaximumPasswordLength = 128;

    public bool PasswordRequireDigit { get; set; }
    public int PasswordRequiredLength { get; set; }
    public bool PasswordRequireNonAlphanumeric { get; set; }
    public bool PasswordRequireUppercase { get; set; }
    public bool PasswordRequireLowercase { get; set; }
}
