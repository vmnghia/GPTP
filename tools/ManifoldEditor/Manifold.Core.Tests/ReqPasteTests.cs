using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

/// <summary>Copy and paste of requirement records (requirements UX spec §4).</summary>
public class ReqPasteTests
{
    static Requirements Vanilla() => VanillaRequirements.FromBlock(Fixtures.Bytes("requirement-tables.bin"));

    static (ButtonSetDocument Doc, EditorState State) Open()
    {
        var project = RequirementsImport.FromProject(FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp")), Vanilla(), new List<string>());
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(),
            requirements: new RequirementSet(project, project, Vanilla(), null));
        return (doc, new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()));
    }

    [Fact]
    public void PasteWithinUnits()
    {
        var (doc, state) = Open();
        var firebat = doc.Requirements![ReqTable.Units].Find(32)!;
        state.CopyRequirement(ReqTable.Units, 0);
        Assert.True(state.HasCopiedRequirement);
        Assert.Null(state.PasteRequirement(ReqTable.Units, 32, "Firebat"));
        var pasted = doc.Requirements![ReqTable.Units].Find(32)!;
        var marine = doc.Requirements[ReqTable.Units].Find(0)!;
        Assert.Equal(32, pasted.Id);
        Assert.Equal(marine.ToWords(), pasted.ToWords());
        Assert.Equal(marine.Used, pasted.Used);
        Assert.Equal("Paste onto Firebat's requirements", doc.UndoLabel);
        state.Undo();
        Assert.True(doc.Requirements![ReqTable.Units].Find(32)!.SameAs(firebat));
    }

    [Fact]
    public void PasteAcrossSharedParser()
    {
        var (doc, state) = Open();
        state.CopyRequirement(ReqTable.TechResearch, 0);
        Assert.Null(state.PasteRequirement(ReqTable.TechUse, 3, "Spider Mines"));
        Assert.Equal(doc.Requirements![ReqTable.TechResearch].Find(0)!.ToWords(), doc.Requirements[ReqTable.TechUse].Find(3)!.ToWords());
    }

    [Fact]
    public void RefusedIntoOrders()
    {
        var (doc, state) = Open();
        var before = doc.Requirements!;
        state.CopyRequirement(ReqTable.Units, 5);   // the Siege Tank: FF04 Machine Shop
        Assert.Equal("Orders can't read FF04 (Has add-on)", state.PasteRequirement(ReqTable.Orders, 0, "Die"));
        Assert.True(doc.Requirements!.SameAs(before));
    }

    [Fact]
    public void LevelsOnlyIntoUpgrades()
    {
        var (_, state) = Open();
        state.CopyRequirement(ReqTable.Upgrades, 0);
        Assert.Equal("Only upgrades have levels", state.PasteRequirement(ReqTable.TechResearch, 5, "Siege Mode"));
    }

    [Fact]
    public void PasteSameIsNoStep()
    {
        var (doc, state) = Open();
        state.CopyRequirement(ReqTable.Units, 0);
        Assert.Null(state.PasteRequirement(ReqTable.Units, 0, "Marine"));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void NothingCopied()
    {
        var (_, state) = Open();
        Assert.Equal("Nothing copied", state.PasteRequirement(ReqTable.Units, 0, "Marine"));
        state.CopyRequirement(ReqTable.Units, 91);   // no record
        Assert.False(state.HasCopiedRequirement);
    }

    [Fact]
    public void PasteAddsRecord()
    {
        var (doc, state) = Open();
        state.CopyRequirement(ReqTable.Units, 0);
        Assert.Null(state.PasteRequirement(ReqTable.Units, 91, "Viking"));
        Assert.NotNull(doc.Requirements![ReqTable.Units].Find(91));
    }
}
