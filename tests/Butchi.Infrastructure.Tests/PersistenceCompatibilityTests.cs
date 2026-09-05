using System.Text.Json;
using Butchi.Core.Configuration;
using Butchi.Core.History;
using Butchi.Infrastructure;
using Xunit;

namespace Butchi.Infrastructure.Tests;

public sealed class PersistenceCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "butchi-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void App_paths_preserve_existing_layout_and_repo_sanitization()
    {
        var paths = new AppPaths(_root);

        Assert.Equal(Path.Combine(_root, "config.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(_root, "history.db"), paths.HistoryDbPath);
        Assert.Equal(Path.Combine(_root, "history.json"), paths.LegacyHistoryPath);
        Assert.Equal(
            Path.Combine(_root, "models", "unsloth__Qwen3.5-0.8B-GGUF", "Qwen3.5-0.8B-Q4_K_M.gguf"),
            paths.ModelPath("unsloth/Qwen3.5-0.8B-GGUF", "Qwen3.5-0.8B-Q4_K_M.gguf"));
    }

    [Fact]
    public async Task Config_store_loads_partial_legacy_camelCase_json_with_current_defaults()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "config.json"),
            """{"translateEnabled":true,"rewriteEnabled":true,"targetLanguage":"English"}""");

        var store = new JsonConfigStore(new AppPaths(_root));
        var config = await store.LoadAsync();

        Assert.Equal("English", config.TargetLanguage);
        Assert.Equal(["Vietnamese", "English"], config.FavoriteLanguages);
        Assert.Equal(ResultAction.Copy, config.ResultAction);
        Assert.Equal(BackendPreference.Auto, config.BackendPreference);
        Assert.Equal(6u, config.PopoverHideSeconds);
    }

    [Fact]
    public async Task Config_store_defaults_launch_at_login_to_false_for_legacy_json()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "config.json"),
            """{"targetLanguage":"English"}""");

        var config = await new JsonConfigStore(new AppPaths(_root)).LoadAsync();

        Assert.False(config.LaunchAtLogin);
    }

    [Fact]
    public async Task Config_store_round_trips_launch_at_login()
    {
        var store = new JsonConfigStore(new AppPaths(_root));
        await store.SaveAsync(AppConfig.Default with { LaunchAtLogin = true });

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(_root, "config.json")));

        Assert.True(document.RootElement.GetProperty("launchAtLogin").GetBoolean());
        Assert.True((await store.LoadAsync()).LaunchAtLogin);
    }

    [Fact]
    public async Task Config_store_round_trips_reference_property_names_and_string_values()
    {
        var store = new JsonConfigStore(new AppPaths(_root));
        var config = AppConfig.Default with
        {
            TargetLanguage = "Japanese",
            FavoriteLanguages = ["English", "Japanese", "German"],
            ResultAction = ResultAction.Replace,
            BackendPreference = BackendPreference.Cpu,
            PopoverHideSeconds = 12
        };

        await store.SaveAsync(config);
        var json = await File.ReadAllTextAsync(Path.Combine(_root, "config.json"));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Japanese", root.GetProperty("targetLanguage").GetString());
        Assert.Equal("replace", root.GetProperty("resultAction").GetString());
        Assert.Equal("cpu", root.GetProperty("backendPreference").GetString());
        Assert.Equal(12u, root.GetProperty("popoverHideSeconds").GetUInt32());

        var restored = await store.LoadAsync();
        Assert.Equal(config.TranslateEnabled, restored.TranslateEnabled);
        Assert.Equal(config.RewriteEnabled, restored.RewriteEnabled);
        Assert.Equal(config.TargetLanguage, restored.TargetLanguage);
        Assert.Equal(config.FavoriteLanguages, restored.FavoriteLanguages);
        Assert.Equal(config.RewriteSystemPrompt, restored.RewriteSystemPrompt);
        Assert.Equal(config.TranslateSystemPrompt, restored.TranslateSystemPrompt);
        Assert.Equal(config.ResultAction, restored.ResultAction);
        Assert.Equal(config.BackendPreference, restored.BackendPreference);
        Assert.Equal(config.ModelRepo, restored.ModelRepo);
        Assert.Equal(config.ModelFile, restored.ModelFile);
        Assert.Equal(config.MaxTokens, restored.MaxTokens);
        Assert.Equal(config.Temperature, restored.Temperature);
        Assert.Equal(config.GpuLayers, restored.GpuLayers);
        Assert.Equal(config.HistoryRetentionDays, restored.HistoryRetentionDays);
        Assert.Equal(config.PopoverHideSeconds, restored.PopoverHideSeconds);
    }

    [Fact]
    public async Task Config_status_distinguishes_missing_ready_and_invalid_content()
    {
        var paths = new AppPaths(_root);
        var store = new JsonConfigStore(paths);

        var missing = await store.LoadWithStatusAsync();
        Assert.Equal(ConfigLoadState.Missing, missing.State);
        Assert.Equal("Vietnamese", missing.Config.TargetLanguage);
        Assert.Equal(["Vietnamese", "English"], missing.Config.FavoriteLanguages);
        Assert.Null(missing.ErrorCode);

        await store.SaveAsync(AppConfig.Default with { TargetLanguage = "Japanese" });
        var ready = await store.LoadWithStatusAsync();
        Assert.Equal(ConfigLoadState.Ready, ready.State);
        Assert.Equal("Japanese", ready.Config.TargetLanguage);
        Assert.Null(ready.ErrorCode);

        await File.WriteAllTextAsync(paths.ConfigPath, "{not-json");
        var invalid = await store.LoadWithStatusAsync();
        Assert.Equal(ConfigLoadState.Invalid, invalid.State);
        Assert.Equal("Vietnamese", invalid.Config.TargetLanguage);
        Assert.Equal(["Vietnamese", "English"], invalid.Config.FavoriteLanguages);
        Assert.Equal(nameof(JsonException), invalid.ErrorCode);
    }

    [Fact]
    public async Task Config_status_maps_read_failure_without_exposing_exception_message()
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.ConfigPath, "{}");
        var store = new JsonConfigStore(paths, _ => throw new IOException("private config content"));

        var result = await store.LoadWithStatusAsync();

        Assert.Equal(ConfigLoadState.Unavailable, result.State);
        Assert.Equal("Vietnamese", result.Config.TargetLanguage);
        Assert.Equal(["Vietnamese", "English"], result.Config.FavoriteLanguages);
        Assert.Equal(nameof(IOException), result.ErrorCode);
        Assert.DoesNotContain("private config content", result.ErrorCode);
    }

    [Theory]
    [InlineData("{\"targetLanguage\":null}")]
    [InlineData("{\"targetLanguage\":\"   \"}")]
    [InlineData("{\"modelRepo\":null}")]
    [InlineData("{\"modelFile\":\"\"}")]
    [InlineData("{\"favoriteLanguages\":null}")]
    public async Task Config_status_rejects_semantically_unusable_values(string json)
    {
        var paths = new AppPaths(_root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.ConfigPath, json);

        var result = await new JsonConfigStore(paths).LoadWithStatusAsync();

        Assert.Equal(ConfigLoadState.Invalid, result.State);
        Assert.Equal(AppConfig.Default.TargetLanguage, result.Config.TargetLanguage);
        Assert.Equal(AppConfig.Default.ModelRepo, result.Config.ModelRepo);
        Assert.Equal(AppConfig.Default.ModelFile, result.Config.ModelFile);
        Assert.Equal("InvalidConfiguration", result.ErrorCode);
    }

    [Fact]
    public async Task History_search_filters_case_insensitively_orders_descending_and_clamps_limits()
    {
        var store = new SqliteHistoryStore(new AppPaths(_root));
        await store.AppendAsync(Entry("1", 10, "rewrite", "Hello World", "Fixed text"));
        await store.AppendAsync(Entry("2", 30, "translate", "Good morning", "Xin chao", "Vietnamese"));
        await store.AppendAsync(Entry("3", 20, "translate", "HELLO again", "Xin chao lan nua", "Vietnamese"));

        Assert.Equal(["2", "3", "1"], (await store.SearchAsync()).Select(x => x.Id).ToArray());
        Assert.Equal(2, (await store.SearchAsync(query: "hello")).Count);
        Assert.Equal(2, (await store.SearchAsync(query: "xin CHAO", action: " TRANSLATE ")).Count);
        Assert.Single(await store.SearchAsync(limit: 1));
        Assert.Single(await store.SearchAsync(limit: 0));
        Assert.Equal(3, (await store.SearchAsync(limit: 9999)).Count);
    }

    [Fact]
    public async Task History_retention_matches_forever_disabled_and_day_cutoff_semantics()
    {
        var store = new SqliteHistoryStore(new AppPaths(_root));
        await store.AppendAsync(Entry("old", 1_000, "rewrite", "a", "b"));
        await store.AppendAsync(Entry("new", 200_000_000, "rewrite", "a", "b"));

        await store.ApplyRetentionAsync(-1, 200_000_000);
        Assert.Equal(2, (await store.SearchAsync()).Count);

        await store.ApplyRetentionAsync(1, 200_000_000);
        Assert.Equal(["new"], (await store.SearchAsync()).Select(x => x.Id).ToArray());

        await store.ApplyRetentionAsync(30, 1);
        Assert.Single(await store.SearchAsync());

        await store.ApplyRetentionAsync(0, 200_000_000);
        Assert.Empty(await store.SearchAsync());
    }

    [Fact]
    public async Task Legacy_json_import_ignores_duplicate_ids_preserves_language_and_renames_after_success()
    {
        Directory.CreateDirectory(_root);
        var legacy = new[]
        {
            Entry("same", 1, "translate", "hello", "xin chao", "Vietnamese"),
            Entry("same", 2, "rewrite", "other", "other")
        };
        await File.WriteAllTextAsync(
            Path.Combine(_root, "history.json"),
            JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var store = new SqliteHistoryStore(new AppPaths(_root));
        await store.InitializeAsync();

        var rows = await store.SearchAsync();
        Assert.Single(rows);
        Assert.Equal("Vietnamese", rows[0].TargetLanguage);
        Assert.False(File.Exists(Path.Combine(_root, "history.json")));
        Assert.True(File.Exists(Path.Combine(_root, "history.migrated.json")));
    }

    private static HistoryEntry Entry(
        string id,
        long timestampMs,
        string action,
        string source,
        string result,
        string? targetLanguage = null) =>
        new(id, timestampMs, action, source, result, "ok", targetLanguage);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
