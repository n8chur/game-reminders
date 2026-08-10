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
}
