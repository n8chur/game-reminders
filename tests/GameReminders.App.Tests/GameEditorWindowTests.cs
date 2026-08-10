using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class GameEditorWindowTests
{
    [Fact]
    public void EditorStaysOpenWhenCatalogSaveFails()
    {
        Assert.False(GameEditorWindow.SaveSucceeded("iCloud file is locked"));
    }

    [Fact]
    public void EditorClosesAfterCatalogSaveSucceeds()
    {
        Assert.True(GameEditorWindow.SaveSucceeded(null));
    }

    [Fact]
    public void SelectingDetectedPathsAddsAllUniqueExecutables()
    {
        var result = GameEditorWindow.MergeExecutablePaths(
            "Everwind\\Everwind.exe",
            ["Everwind\\Binaries\\Everwind-Win64-Shipping.exe", " EVERWIND\\EVERWIND.EXE "]);

        Assert.Equal(2, result.Count);
        Assert.Equal("Everwind\\Everwind.exe", result[0]);
        Assert.Equal("Everwind\\Binaries\\Everwind-Win64-Shipping.exe", result[1]);
    }

    [Fact]
    public void UnresolvedSteamGameCannotSaveWithoutExecutable()
    {
        var source = new GameSource { Type = "steam", RequiresExecutableReview = true };

        Assert.False(GameEditorWindow.CanSave(source, []));
        Assert.True(GameEditorWindow.CanSave(source, ["Everwind\\Everwind.exe"]));
    }

    [Fact]
    public void ManualGameMaySaveWithoutExecutable()
    {
        var source = new GameSource { Type = "manual" };

        Assert.True(GameEditorWindow.CanSave(source, []));
    }
}
