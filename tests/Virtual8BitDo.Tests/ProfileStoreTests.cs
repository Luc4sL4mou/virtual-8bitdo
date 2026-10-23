using Virtual8BitDo.Core.Profiles;
using Xunit;

namespace Virtual8BitDo.Tests;

/// <summary>Perfis por jogo (etapa 5): criar, duplicar, renomear, associar .exe, apagar.</summary>
public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v8test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void NewStore_CreatesDefaultProfile()
    {
        var s = new ProfileStore(_dir);
        Assert.Single(s.Profiles);
        Assert.True(s.Default.IsDefault);
        Assert.True(File.Exists(Path.Combine(_dir, "Padrão.json")));
    }

    [Fact]
    public void ForExe_MatchesIgnoringCase_ElseDefault()
    {
        var s = new ProfileStore(_dir);
        var elden = s.Create("Elden Ring", "eldenring.exe");
        Assert.Same(elden, s.ForExe("EldenRing.EXE"));
        Assert.Same(s.Default, s.ForExe("notepad.exe"));
    }

    [Fact]
    public void Duplicate_KeepsRules_ButNotGame()
    {
        var s = new ProfileStore(_dir);
        var elden = s.Create("Elden Ring", "eldenring.exe");
        elden.Set("P4", new ButtonAction { Kind = ActionKind.Key, Key = "F13" });
        var copy = s.Duplicate(elden);
        Assert.Equal("Elden Ring (cópia)", copy.Name);
        Assert.Null(copy.Exe);
        Assert.Equal("F13", copy.Get("P4").Key);
    }

    [Fact]
    public void Rename_ToExistingName_GetsSuffix()
    {
        var s = new ProfileStore(_dir);
        s.Create("Elden Ring", null);
        var other = s.Create("Outro", null);
        s.Rename(other, "Elden Ring");
        Assert.Equal("Elden Ring 2", other.Name);
    }

    [Fact]
    public void SetExe_MovesGameFromOtherProfile()
    {
        var s = new ProfileStore(_dir);
        var a = s.Create("A", "game.exe");
        var b = s.Create("B", null);
        s.SetExe(b, "game.exe");
        Assert.Null(a.Exe);
        Assert.Same(b, s.ForExe("game.exe"));
    }

    [Fact]
    public void Delete_RemovesFile_ButNeverTheDefault()
    {
        var s = new ProfileStore(_dir);
        var p = s.Create("Temp", null);
        s.Delete(p);
        Assert.False(File.Exists(Path.Combine(_dir, "Temp.json")));
        Assert.Throws<InvalidOperationException>(() => s.Delete(s.Default));
    }

    [Fact]
    public void Reload_KeepsProfilesAndRules()
    {
        var s = new ProfileStore(_dir);
        var p = s.Create("Jogo", "jogo.exe");
        p.Set("P1", new ButtonAction { Kind = ActionKind.Xbox, Xbox = { "A" }, Turbo = true });
        s.Save(p);

        var reloaded = new ProfileStore(_dir).ForExe("jogo.exe");
        Assert.Equal("Jogo", reloaded.Name);
        Assert.True(reloaded.Get("P1").Turbo);
        Assert.Equal(new[] { "A" }, reloaded.Get("P1").Xbox);
    }
}
