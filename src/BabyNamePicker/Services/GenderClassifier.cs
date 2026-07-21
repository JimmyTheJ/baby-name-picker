using BabyNamePicker.Models;

namespace BabyNamePicker.Services;

public static class GenderClassifier
{
    private const double UnisexThreshold = 0.15;

    public static (BabyGender Gender, double MaleShare) Classify(long maleCount, long femaleCount)
    {
        var total = maleCount + femaleCount;
        if (total == 0)
        {
            return (BabyGender.Unisex, 0.5);
        }

        var maleShare = (double)maleCount / total;

        if (maleCount == 0)
        {
            return (BabyGender.Female, 0);
        }

        if (femaleCount == 0)
        {
            return (BabyGender.Male, 1);
        }

        var minorityShare = Math.Min(maleShare, 1 - maleShare);
        if (minorityShare >= UnisexThreshold)
        {
            return (BabyGender.Unisex, maleShare);
        }

        return maleShare >= 0.5
            ? (BabyGender.Male, maleShare)
            : (BabyGender.Female, maleShare);
    }
}
