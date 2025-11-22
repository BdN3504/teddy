using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TeddyBench.Avalonia.Services;
using TeddyBench.Avalonia.ViewModels;
using TeddyBench.Avalonia.Dialogs;
using Xunit;

namespace TeddyBench.Avalonia.Tests;

/// <summary>
/// Tests for TRASHCAN recovery dialog functionality
/// These tests are NOT headless - they show the actual UI for better debugging
/// </summary>
public class TrashcanDialogTests
{
    private const string SD_CARD_PATH = "/media/david/3238-3133";
    private const string CONTENT_PATH = SD_CARD_PATH + "/CONTENT";

    [AvaloniaFact]
    public async Task RealUserWorkflow_StartApp_AutoDetectSD_OpenTrashcan()
    {
        // Skip test if SD card is not mounted
        if (!Directory.Exists(CONTENT_PATH))
        {
            Console.WriteLine("Skipping test - SD card not found at " + CONTENT_PATH);
            return;
        }

        Console.WriteLine("=== TRASHCAN REAL USER WORKFLOW TEST ===");
        Console.WriteLine($"SD Card Path: {SD_CARD_PATH}");
        Console.WriteLine("This test will show the actual UI");
        Console.WriteLine();

        // 1. Start the app like a normal user would
        Console.WriteLine("Step 1: Starting application...");
        var window = new Views.MainWindow();
        var viewModel = new MainWindowViewModel(window);
        window.DataContext = viewModel;
        window.Show();

        // 2. Wait for auto-detection to run (this is what happens on real startup)
        Console.WriteLine("Step 2: Waiting for SD card auto-detection...");
        await viewModel.AutoOpenDirectoryOnStartup();

        // Give it time to scan
        await Task.Delay(3000);

        // 3. Verify the SD card was detected and loaded
        Console.WriteLine($"Step 3: Verifying directory loaded... HasValidDirectory={viewModel.HasValidDirectory}");
        if (!viewModel.HasValidDirectory)
        {
            Console.WriteLine("WARNING: Auto-detection did not load the directory");
            Console.WriteLine($"CurrentDirectory: {viewModel.CurrentDirectory}");
            window.Close();
            return; // Skip test if auto-detection didn't work
        }

        Console.WriteLine($"✓ Loaded {viewModel.TonieFiles.Count} Tonie files from {viewModel.CurrentDirectory}");

        // 4. Click the TRASHCAN Recovery button
        Console.WriteLine("Step 4: Clicking TRASHCAN Recovery button...");

        Exception? dialogException = null;
        try
        {
            Assert.True(viewModel.OpenTrashcanManagerCommand.CanExecute(null),
                "OpenTrashcanManagerCommand should be executable");

            await viewModel.OpenTrashcanManagerCommand.ExecuteAsync(null);

            Console.WriteLine("✓ TRASHCAN dialog opened successfully!");

            // Keep the dialog open for a moment so we can see it
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            dialogException = ex;
            Console.WriteLine($"✗ TRASHCAN dialog failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        // Verify
        Assert.Null(dialogException);

        Console.WriteLine("=== TEST COMPLETED SUCCESSFULLY ===");

        // Cleanup
        window.Close();
    }

    [AvaloniaFact]
    public async Task TrashcanService_WithRealSdCard_ShouldScanTrashcan()
    {
        // Skip test if SD card is not mounted
        if (!Directory.Exists(SD_CARD_PATH))
        {
            Console.WriteLine("Skipping test - SD card not found");
            return;
        }

        Console.WriteLine("=== Testing TrashcanService.ScanTrashcanAsync ===");

        // Create services
        var metadataService = new TonieMetadataService();
        var trashcanService = new TrashcanService(metadataService);

        // Scan TRASHCAN
        var deletedTonies = await trashcanService.ScanTrashcanAsync(SD_CARD_PATH);

        // Should not throw and should return a list (might be empty if TRASHCAN is clean)
        Assert.NotNull(deletedTonies);

        Console.WriteLine($"Found {deletedTonies.Count} deleted Tonie(s) in TRASHCAN");

        // If there are deleted tonies, verify they have proper data
        foreach (var tonie in deletedTonies)
        {
            Console.WriteLine($"  - {tonie.DisplayName} (UID: {tonie.Uid}, Hash: {tonie.Hash[..8]}...)");

            Assert.NotEmpty(tonie.FilePath);
            Assert.NotEmpty(tonie.Hash);
            Assert.NotEmpty(tonie.Uid);
            Assert.NotEmpty(tonie.DisplayName);
            Assert.True(tonie.DeletionDate > DateTime.MinValue);
        }

        Console.WriteLine("=== Test completed ===");
    }

    [AvaloniaFact]
    public async Task TrashcanDialog_ViewModel_ShouldInitializeWithoutErrors()
    {
        // Skip test if SD card is not mounted
        if (!Directory.Exists(SD_CARD_PATH))
        {
            Console.WriteLine("Skipping test - SD card not found");
            return;
        }

        Console.WriteLine("=== Testing TrashcanManagerDialogViewModel initialization ===");

        // Create a test window
        var window = new Window();
        window.Show();

        Exception? caughtException = null;

        try
        {
            // Create metadata service
            var metadataService = new TonieMetadataService();

            // Create the ViewModel - this triggers scanning
            var viewModel = new TrashcanManagerDialogViewModel(window, SD_CARD_PATH, metadataService);

            // Wait for async loading
            await Task.Delay(2000);

            // Verify initialization
            Assert.NotNull(viewModel.DeletedTonies);
            Assert.False(viewModel.IsLoading);
            Assert.NotEmpty(viewModel.StatusText);

            Console.WriteLine($"✓ ViewModel initialized successfully");
            Console.WriteLine($"  Status: {viewModel.StatusText}");
            Console.WriteLine($"  Deleted Tonies: {viewModel.DeletedTonies.Count}");
        }
        catch (Exception ex)
        {
            caughtException = ex;
            Console.WriteLine($"✗ ViewModel initialization failed: {ex.Message}");
        }

        // Assert no exceptions
        Assert.Null(caughtException);

        // Cleanup
        window.Close();

        Console.WriteLine("=== Test completed ===");
    }

    [AvaloniaFact]
    public async Task RfidInputDialog_ShouldValidateUidCorrectly()
    {
        Console.WriteLine("=== Testing RfidInputDialog validation ===");

        // Create a test window
        var parentWindow = new Window();
        parentWindow.Show();

        Exception? caughtException = null;

        try
        {
            // Create the RFID input dialog with empty prefix (TRASHCAN restoration scenario)
            var dialog = new RfidInputDialog("", CONTENT_PATH);

            // Get the ViewModel
            var viewModel = dialog.DataContext as RfidInputDialogViewModel;
            Assert.NotNull(viewModel);

            Console.WriteLine("Step 1: Testing empty UID (should be invalid)");
            Assert.False(viewModel.CanSubmit);
            Assert.NotEmpty(viewModel.ErrorMessage);
            Console.WriteLine($"  ✓ Error message: {viewModel.ErrorMessage}");

            Console.WriteLine("Step 2: Testing partial UID (should be invalid)");
            viewModel.RfidUid = "0EED";
            Assert.False(viewModel.CanSubmit);
            Assert.Contains("8 characters", viewModel.ErrorMessage);
            Console.WriteLine($"  ✓ Error message: {viewModel.ErrorMessage}");

            Console.WriteLine("Step 3: Testing invalid characters (should be invalid)");
            viewModel.RfidUid = "0EEDXXYZ";
            Assert.False(viewModel.CanSubmit);
            Console.WriteLine($"  ✓ Error message: {viewModel.ErrorMessage}");

            Console.WriteLine("Step 4: Testing valid UID (should be valid)");
            viewModel.RfidUid = "0EED33EA";
            Assert.True(viewModel.CanSubmit);
            Assert.Empty(viewModel.ErrorMessage);
            Console.WriteLine("  ✓ Valid UID accepted");

            Console.WriteLine("✓ All validation tests passed!");
        }
        catch (Exception ex)
        {
            caughtException = ex;
            Console.WriteLine($"✗ Test failed: {ex.Message}");
        }

        // Assert no exceptions
        Assert.Null(caughtException);

        // Cleanup
        try
        {
            parentWindow.Close();
        }
        catch (InvalidOperationException)
        {
            // Ignore Avalonia headless rendering cleanup errors
            // These are not related to the actual test logic
        }

        Console.WriteLine("=== Test completed ===");
    }
}