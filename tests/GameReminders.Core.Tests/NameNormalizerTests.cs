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
    [Fact]
    public void PortableAndAbsolutePathsMatchButDifferentGameFoldersDoNotOverlap()
    {
        Assert.True(NameNormalizer.ExecutableMappingsOverlap(
            @"Everwind\Everwind.exe",
            @"D:\SteamLibrary\steamapps\common\Everwind\Everwind.exe"));
        Assert.False(NameNormalizer.ExecutableMappingsOverlap(
            @"FirstGame\Binaries\Game.exe",
            @"SecondGame\Binaries\Game.exe"));
    }

    [Fact]
    public void FilenameAndPathMappingsForSameExecutableOverlap()
    {
        Assert.True(NameNormalizer.ExecutableMappingsOverlap(
            "Everwind.exe",
            @"Everwind\Everwind.exe"));
        Assert.False(NameNormalizer.ExecutableMappingsOverlap(
            "EverwindLauncher.exe",
            @"Everwind\Everwind.exe"));
    }

    [Fact]
    public void FilenameMappingMatchesObservedAbsolutePath()
    {
        Assert.True(NameNormalizer.ExecutableMatches(
            "Everwind.exe",
            @"D:\SteamLibrary\steamapps\common\Everwind\Everwind.exe"));
    }
}
