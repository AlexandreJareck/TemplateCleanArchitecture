namespace Template.Application.Settings
{
    public class LocalizationSettings
    {
        public string DefaultRequestCulture { get; init; } = string.Empty;
        public List<string> SupportedCultures { get; init; } = [];
    }
}
