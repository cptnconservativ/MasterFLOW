using MasterFlow.Desktop.ViewModels;
using MasterFlow.Persistence;

namespace MasterFlow.Desktop.Tests;

/// <summary>The start-up recovery prompt never deletes work unless the user explicitly picks Discard.</summary>
public class RecoveryPromptTests
{
    private sealed class FakeDialogs(RecoveryChoice choice) : IDialogService
    {
        public string? LastRecoveryMessage { get; private set; }
        public Task<RecoveryChoice> AskRecoveryAsync(string message) { LastRecoveryMessage = message; return Task.FromResult(choice); }
        public Task<string?> PickOpenProjectAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickSaveAsync(string suggestedName, string extension, string description, params (string Extension, string Description)[] alternatives) => Task.FromResult<string?>(null);
        public Task SetClipboardAsync(string text) => Task.CompletedTask;
        public void OpenWithShell(string path) { }
        public Task<bool> ConfirmDiscardAsync(string message) => Task.FromResult(true);
        public Task<string?> PickImageAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickOpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);
    }

    private static (string Dir, UserDataStore Store) Crashed(string title)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mf-rec-" + Guid.NewGuid().ToString("N"));
        var store = new UserDataStore(dir);
        var vm = MainViewModel.CreateWithSample();
        vm.Store = store;
        vm.Selected!.Title = title;
        Assert.True(vm.Autosave());
        return (dir, store);
    }

    [Fact]
    public async Task KeepForLater_MovesTheWorkAside_InsteadOfDeletingIt()
    {
        var (dir, store) = Crashed("Before the crash");
        try
        {
            var vm = MainViewModel.CreateWithSample();
            vm.Store = store;
            var dialogs = new FakeDialogs(RecoveryChoice.SetAside);
            vm.Dialogs = dialogs;
            Assert.False(await vm.CheckRecoveryAsync());
            Assert.Contains("Discard deletes it", dialogs.LastRecoveryMessage);
            Assert.Null(store.FindRecovery());
            var kept = Directory.GetFiles(store.RecoveredDirectory).Single();
            Assert.Contains("Before the crash", ProjectFile.Load(kept).Calculations.Select(c => c.Title));
            Assert.Contains(kept, vm.StatusMessage);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Discard_DeletesOnlyWhenChosen()
    {
        var (dir, store) = Crashed("Throw away");
        try
        {
            var vm = MainViewModel.CreateWithSample();
            vm.Store = store;
            vm.Dialogs = new FakeDialogs(RecoveryChoice.Discard);
            Assert.False(await vm.CheckRecoveryAsync());
            Assert.Null(store.FindRecovery());
            Assert.False(Directory.Exists(store.RecoveredDirectory));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Restore_LoadsTheWork()
    {
        var (dir, store) = Crashed("Bring me back");
        try
        {
            var vm = MainViewModel.CreateWithSample();
            vm.Store = store;
            vm.Dialogs = new FakeDialogs(RecoveryChoice.Restore);
            Assert.True(await vm.CheckRecoveryAsync());
            Assert.Contains(vm.Calculations, c => c.Title == "Bring me back");
            Assert.True(vm.IsDirty);
        }
        finally { Directory.Delete(dir, true); }
    }
}
