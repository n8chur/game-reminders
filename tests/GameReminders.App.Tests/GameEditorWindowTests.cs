using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class GameEditorWindowTests
{
    [Fact]
    public void SaveButtonTextFollowsEnabledAndDisabledForegroundsInTheLightTheme()
    {
        System.Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                var editor = new GameEditorWindow(
                    new GameDefinition { Id = "test", Name = "Test" },
                    _ => null);

                var enabledButtonForeground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                    editor.SaveButton.Foreground);
                var enabledTextForeground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                    editor.SaveButtonText.Foreground);
                Assert.Equal(System.Windows.Media.Colors.White, enabledButtonForeground.Color);
                Assert.Equal(enabledButtonForeground.Color, enabledTextForeground.Color);

                editor.SaveButton.IsEnabled = false;

                var disabledButtonForeground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                    editor.SaveButton.Foreground);
                var disabledTextForeground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                    editor.SaveButtonText.Foreground);
                Assert.NotEqual(System.Windows.Media.Colors.White, disabledButtonForeground.Color);
                Assert.Equal(disabledButtonForeground.Color, disabledTextForeground.Color);

                editor.Close();
                application.Shutdown();
            }
            catch (System.Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

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
    public void AddingDetectedPathAppendsOnlyUniqueExecutables()
    {
        var result = GameEditorWindow.MergeExecutablePaths(
            "Everwind\\Everwind.exe",
            ["Everwind\\Binaries\\Everwind-Win64-Shipping.exe", " EVERWIND\\EVERWIND.EXE "]);

        Assert.Equal(2, result.Count);
        Assert.Equal("Everwind\\Everwind.exe", result[0]);
        Assert.Equal("Everwind\\Binaries\\Everwind-Win64-Shipping.exe", result[1]);
    }

    [Fact]
    public void SelectingDetectedPathReplacesExistingExecutables()
    {
        var result = GameEditorWindow.ReplaceExecutablePaths(
            [" Mistfall Hunter\\MistfallHunter.exe "]);

        Assert.Equal("Mistfall Hunter\\MistfallHunter.exe", Assert.Single(result));
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
