using System.Text.RegularExpressions;

namespace PersonalTracker.Application.Services;

internal static partial class CoverRules
{
    [GeneratedRegex("^[a-z0-9-]{1,32}$")] private static partial Regex GradientKey();
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex HexColor();

    public static void Validate(CoverInput cover, ErrorBag errors)
    {
        switch (cover.Type)
        {
            case CoverType.None:
                break;
            case CoverType.Gradient:
                if (cover.Value is null || !GradientKey().IsMatch(cover.Value))
                    errors.Add("cover.value", "Choose a valid gradient.");
                break;
            case CoverType.Color:
                if (cover.Value is null || !HexColor().IsMatch(cover.Value))
                    errors.Add("cover.value", "Use a colour like #1F6F78.");
                break;
            default:
                errors.Add("cover.type", "Images are uploaded separately.");
                break;
        }
    }
}
