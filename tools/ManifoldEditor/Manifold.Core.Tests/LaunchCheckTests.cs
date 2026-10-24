using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class LaunchCheckTests
{
    static RepackPending Pending(bool differs = false, bool newer = false, int files = 0) => new(differs, newer, files, "");

    [Fact]
    public void Nothing_to_ask() =>
        Assert.Equal(new LaunchQuestions(false, null), LaunchCheck.Questions(false, Pending(), "GPTP.qdp"));

    [Fact]
    public void Dirty_asks_save() => Assert.True(LaunchCheck.Questions(true, Pending(), "GPTP.qdp").AskSave);

    [Fact]
    public void Plugin_and_files_texts()
    {
        Assert.Equal("GPTP.qdp is newer than the exe's plugin.", LaunchCheck.Questions(false, Pending(true, true), "GPTP.qdp").RepackText);
        Assert.Equal("1 to-repack file is new or changed.", LaunchCheck.Questions(false, Pending(files: 1), "GPTP.qdp").RepackText);
        Assert.Equal("2 to-repack files are new or changed.", LaunchCheck.Questions(false, Pending(files: 2), "GPTP.qdp").RepackText);
        Assert.Equal("GPTP.qdp is newer than the exe's plugin, and 2 to-repack files are new or changed.",
            LaunchCheck.Questions(false, Pending(true, true, 2), "GPTP.qdp").RepackText);
        Assert.Equal("GPTP.qdp is newer than the exe's plugin, and 1 to-repack file is new or changed.",
            LaunchCheck.Questions(false, Pending(true, true, 1), "GPTP.qdp").RepackText);
    }

    [Fact]
    public void An_older_plugin_asks_nothing() =>
        Assert.Null(LaunchCheck.Questions(false, Pending(differs: true), "GPTP.qdp").RepackText);

    [Fact]
    public void No_pending_check_asks_only_save() =>
        Assert.Equal(new LaunchQuestions(true, null), LaunchCheck.Questions(true, null, "GPTP.qdp"));
}
