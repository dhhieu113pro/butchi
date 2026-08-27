using Butchi.App.History;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.History;
using Xunit;

namespace Butchi.App.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task Refresh_passes_search_filter_and_limit_to_store()
    {
        var store = new FakeHistoryStore();
        var vm = await HistoryViewModel.CreateAsync(store, new FakeClipboard(), new FakeConfigStore(AppConfig.Default), CancellationToken.None);
        vm.Query = "hello";
        vm.ActionFilter = "translate";
        vm.Limit = 25;

        await vm.RefreshAsync(CancellationToken.None);

        Assert.Equal("hello", store.LastQuery);
        Assert.Equal("translate", store.LastAction);
        Assert.Equal(25, store.LastLimit);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_exposes_empty_and_error_states()
    {
        var store = new FakeHistoryStore();
        var vm = await HistoryViewModel.CreateAsync(store, new FakeClipboard(), new FakeConfigStore(AppConfig.Default), CancellationToken.None);
        Assert.True(vm.IsEmpty);

        store.SearchError = new InvalidOperationException("history unavailable");
        await vm.RefreshAsync(CancellationToken.None);

        Assert.Empty(vm.Items);
        Assert.Equal("history unavailable", vm.ErrorMessage);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Delete_removes_entry_and_refreshes_results()
    {
        var entry = Entry("1", "input", "output");
        var store = new FakeHistoryStore(entry);
        var vm = await HistoryViewModel.CreateAsync(store, new FakeClipboard(), new FakeConfigStore(AppConfig.Default), CancellationToken.None);

        await vm.DeleteAsync(entry, CancellationToken.None);

        Assert.Equal("1", store.DeletedId);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Clear_requires_explicit_confirmation()
    {
        var store = new FakeHistoryStore(Entry("1", "input", "output"));
        var vm = await HistoryViewModel.CreateAsync(store, new FakeClipboard(), new FakeConfigStore(AppConfig.Default), CancellationToken.None);

        await vm.ClearAsync(confirmed: false, CancellationToken.None);
        Assert.Equal(0, store.ClearCalls);

        await vm.ClearAsync(confirmed: true, CancellationToken.None);
        Assert.Equal(1, store.ClearCalls);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Copy_can_copy_source_or_result()
    {
        var clipboard = new FakeClipboard();
        var vm = await HistoryViewModel.CreateAsync(new FakeHistoryStore(), clipboard, new FakeConfigStore(AppConfig.Default), CancellationToken.None);
        var entry = Entry("1", "source text", "result text");

        await vm.CopySourceAsync(entry, CancellationToken.None);
        Assert.Equal("source text", clipboard.Text);

        await vm.CopyResultAsync(entry, CancellationToken.None);
        Assert.Equal("result text", clipboard.Text);
    }

    [Fact]
    public async Task Retention_is_persisted_and_applied()
    {
        var store = new FakeHistoryStore();
        var config = new FakeConfigStore(AppConfig.Default with { HistoryRetentionDays = 30 });
        var vm = await HistoryViewModel.CreateAsync(store, new FakeClipboard(), config, CancellationToken.None);

        await vm.SetRetentionDaysAsync(7, 1_800_000_000_000L, CancellationToken.None);

        Assert.Equal(7, vm.RetentionDays);
        Assert.Equal(7, config.Value.HistoryRetentionDays);
        Assert.Equal((7, 1_800_000_000_000L), store.LastRetention);
        Assert.Equal("Saved", vm.SaveStatus);
    }

    private static HistoryEntry Entry(string id, string source, string result) =>
        new(id, 1, "translate", source, result, string.Empty, "Vietnamese");

    private sealed class FakeHistoryStore(params HistoryEntry[] initial) : IHistoryStore
    {
        private readonly List<HistoryEntry> _items = [.. initial];
        public string? LastQuery { get; private set; }
        public string? LastAction { get; private set; }
        public int? LastLimit { get; private set; }
        public string? DeletedId { get; private set; }
        public int ClearCalls { get; private set; }
        public Exception? SearchError { get; set; }
        public (int Days, long NowMs)? LastRetention { get; private set; }

        public ValueTask<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, string? action, int? limit, CancellationToken cancellationToken)
        {
            LastQuery = query;
            LastAction = action;
            LastLimit = limit;
            if (SearchError is not null) throw SearchError;
            return ValueTask.FromResult<IReadOnlyList<HistoryEntry>>([.. _items]);
        }

        public ValueTask DeleteAsync(string id, CancellationToken cancellationToken)
        {
            DeletedId = id;
            _items.RemoveAll(x => x.Id == id);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            _items.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask ApplyRetentionAsync(int retentionDays, long nowMs, CancellationToken cancellationToken)
        {
            LastRetention = (retentionDays, nowMs);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeClipboard : IHistoryClipboard
    {
        public string? Text { get; private set; }
        public ValueTask SetTextAsync(string text, CancellationToken cancellationToken)
        {
            Text = text;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Value { get; private set; } = initial;
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);
        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            Value = config;
            return ValueTask.CompletedTask;
        }
    }
}
