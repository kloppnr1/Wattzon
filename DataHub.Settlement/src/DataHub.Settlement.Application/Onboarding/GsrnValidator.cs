namespace DataHub.Settlement.Application.Onboarding;

public static class GsrnValidator
{
    public static bool IsValid(string gsrn)
    {
        return gsrn.Length == 18
            && gsrn.StartsWith("57")
            && gsrn.All(char.IsDigit);
    }
}
