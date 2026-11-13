using AwesomeAssertions;
using ReactiveUI.Primitives;
using ServiceLib.Trisko;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoUpdateViewModelTests
{
    [Fact]
    public void UIItem_UpdateCheckDefaults_ShouldBeEnabledWithTwoHoursInterval()
    {
        var uiItem = new UIItem();

        uiItem.TriskoAutoCheckUpdatesEnabled.Should().BeTrue();
        uiItem.TriskoCheckUpdateIntervalHours.Should().Be(2);
        uiItem.TriskoAutoStartVirtualHereOnLaunch.Should().BeFalse();
    }
    [Fact]
    public async Task InitializeAsync_PortableBuild_ShouldExplainSetupRequirement()
    {
        var service = new FakeUpdateService { IsInstalled = false };
        var viewModel = new TriskoUpdateViewModel(service);

        await viewModel.InitializeAsync(checkImmediately: true);

        viewModel.CanRunAction.Should().BeFalse();
        viewModel.AvailableVersionText.Should().Be("Portable-режим");
        viewModel.StatusText.Should().Contain("Setup.exe");
        service.CheckCallCount.Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_AvailableUpdate_ShouldExposeVersionAndNotes()
    {
        var service = new FakeUpdateService
        {
            IsInstalled = true,
            CheckResult = TriskoUpdateCheckResult.Available,
            AvailableUpdate = new TriskoAvailableUpdate("3.0.1", "Исправлено SMB")
        };
        var viewModel = new TriskoUpdateViewModel(service);

        await viewModel.InitializeAsync(checkImmediately: true);

        viewModel.HasAvailableUpdate.Should().BeTrue();
        viewModel.AvailableVersionText.Should().Contain("3.0.1");
        viewModel.ReleaseNotesText.Should().Be("Исправлено SMB");
        viewModel.ActionButtonText.Should().Be("Скачать и установить");
    }

    [Fact]
    public async Task InitializeAsync_UpToDate_ShouldOfferAnotherCheck()
    {
        var service = new FakeUpdateService
        {
            IsInstalled = true,
            CheckResult = TriskoUpdateCheckResult.UpToDate
        };
        var viewModel = new TriskoUpdateViewModel(service);

        await viewModel.InitializeAsync(checkImmediately: true);

        viewModel.HasAvailableUpdate.Should().BeFalse();
        viewModel.AvailableVersionText.Should().Contain("актуальная");
        viewModel.ActionButtonText.Should().Be("Проверить снова");
    }

    [Fact]
    public async Task RunActionCommand_AvailableUpdate_ShouldDownloadAndApply()
    {
        var service = new FakeUpdateService
        {
            IsInstalled = true,
            CheckResult = TriskoUpdateCheckResult.Available,
            AvailableUpdate = new TriskoAvailableUpdate("3.0.1", "Исправлено SMB")
        };
        var viewModel = new TriskoUpdateViewModel(service);
        await viewModel.InitializeAsync(checkImmediately: true);

        viewModel.RunActionCommand.Execute().Subscribe(new NoopObserver());
        await WaitUntilAsync(() => service.ApplyCallCount == 1);

        service.DownloadCallCount.Should().Be(1);
        service.ApplyCallCount.Should().Be(1);
        viewModel.ErrorMessage.Should().BeNull();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }
    }

    private sealed class NoopObserver : IObserver<RxVoid>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) => throw error;

        public void OnNext(RxVoid value) { }
    }

    private sealed class FakeUpdateService : ITriskoApplicationUpdateService
    {
        public bool IsInstalled { get; init; }
        public string CurrentVersion => "3.0.0";
        public string ReleaseSource => "test/releases";
        public TriskoAvailableUpdate? AvailableUpdate { get; init; }
        public TriskoUpdateCheckResult CheckResult { get; init; }
        public int CheckCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }

        public Task<TriskoUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            return Task.FromResult(CheckResult);
        }

        public Task DownloadAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCallCount++;
            progress?.Report(100);
            return Task.CompletedTask;
        }

        public Task ApplyAndRestartAsync(CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            return Task.CompletedTask;
        }
    }
}
