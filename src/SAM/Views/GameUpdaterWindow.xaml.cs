using System;
using System.Windows;
using SAM.Managers;
using Wpf.Ui.Controls;

namespace SAM.Views;

public partial class GameUpdaterWindow
{
    private readonly GameUpdaterManager _manager;

    public GameUpdaterWindow()
    {
        InitializeComponent();
        _manager = new GameUpdaterManager();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var appId = AppIdTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(appId))
        {
            StatusTextBlock.Text = "Please enter a valid AppID.";
            return;
        }

        StatusTextBlock.Text = $"Updating AppID {appId}...";
        AppIdTextBox.IsEnabled = false;

        try
        {
            bool useMirror = MirrorCheckBox.IsChecked ?? false;
            SAM.Managers.GameUpdateResult result = await _manager.UpdateGameAsync(appId, useMirror);

            if (result == SAM.Managers.GameUpdateResult.Success)
            {
                StatusTextBlock.Text = "Update complete!";
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Success",
                    Content = $"Successfully updated manifests for AppID {appId}.",
                    CloseButtonText = "OK"
                };
                await msgBox.ShowDialogAsync();
                // Close(); // Keep window open as per user request
            }
            else if (result == SAM.Managers.GameUpdateResult.InvalidAppId) 
            {
                StatusTextBlock.Text = "Invalid AppID.";
                // User requested: "AppID invalid or not in database"
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Error",
                    Content = "AppID invalid or not in database.",
                    CloseButtonText = "OK"
                };
                await msgBox.ShowDialogAsync();
            }
            else
            {
                StatusTextBlock.Text = "Update failed.";
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Error",
                    Content = "Failed to download manifests. Please check your internet connection or try again later.",
                    CloseButtonText = "OK"
                };
                await msgBox.ShowDialogAsync();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Error occurred.";
            var msgBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Error",
                Content = $"Failed to update game: {ex.Message}",
                CloseButtonText = "OK"
            };
            await msgBox.ShowDialogAsync();
        }
        finally
        {
            AppIdTextBox.IsEnabled = true;
        }
    }
}
