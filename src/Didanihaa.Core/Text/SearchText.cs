using System.Text;

namespace Didanihaa.Core;

public static class SearchText
{
    public static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC)
        .Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ').Trim().ToUpperInvariant();
}
