using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class NameNormalizerTests
{
    [Theory]
    [InlineData("No Rest for the Wicked", "norestforthewicked")]
    [InlineData("No-Rest-for-the-Wicked", "norestforthewicked")]
    [InlineData("  FAREVER!  ", "farever")]
    public void NormalizeIgnoresCaseSpacesAndPunctuation(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Farever.exe", "farever")]
    [InlineData("C:\\Games\\Farever-Win64-Shipping.EXE", "farever-win64-shipping")]
    public void NormalizeProcessNameRemovesExe(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.NormalizeProcessName(input));
    }

    [Fact]
    public void NormalizeProcessNameRejectsNullExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => NameNormalizer.NormalizeProcessName(null!));
    }

    [Fact]
    public void ExecutableIdentityPreservesDistinguishingPath()
    {
        Assert.NotEqual(
            NameNormalizer.NormalizeExecutableIdentity(@"FirstGame\Binaries\Game.exe"),
            NameNormalizer.NormalizeExecutableIdentity(@"SecondGame\Binaries\Game.exe"));
        Assert.True(NameNormalizer.ExecutablePathMatches(
            @"FirstGame\Binaries\Game.exe",
            @"D:\SteamLibrary\steamapps\common\FirstGame\Binaries\GAME.EXE"));
    }
}
