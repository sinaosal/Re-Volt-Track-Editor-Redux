// Unit tests for the Unity-independent core (Assets/Scripts/Core).
// Run with:  dotnet run --project Tests/Core.Tests
// Deliberately dependency free: a tiny assertion helper instead of a test framework package.

using ReVolt.Track;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal static partial class Program
{
    private static int passed;
    private static readonly List<string> failures = new List<string>();

    private static int Main()
    {
        Run(nameof(RoundTripPreservesContent), RoundTripPreservesContent);
        Run(nameof(WriterMatchesLegacyWriterByteForByte), WriterMatchesLegacyWriterByteForByte);
        Run(nameof(ReadsLegacyUtf8Names), ReadsLegacyUtf8Names);
        Run(nameof(ReadsLatin1Names), ReadsLatin1Names);
        Run(nameof(ReadsVersion1WithoutPickups), ReadsVersion1WithoutPickups);
        Run(nameof(RejectsWrongMagic), RejectsWrongMagic);
        Run(nameof(RejectsUnsupportedVersion), RejectsUnsupportedVersion);
        Run(nameof(RejectsTruncatedCells), RejectsTruncatedCells);
        Run(nameof(RejectsHugeGridWithoutAllocating), RejectsHugeGridWithoutAllocating);
        Run(nameof(RejectsTruncatedPickups), RejectsTruncatedPickups);
        Run(nameof(WarnsAboutPickupsOutsideGrid), WarnsAboutPickupsOutsideGrid);
        Run(nameof(ModuleSpaceAndNullAreEmpty), ModuleSpaceAndNullAreEmpty);
        Run(nameof(SanitizeNameKeepsPrintableAscii), SanitizeNameKeepsPrintableAscii);
        Run(nameof(WriterSanitizesNonAsciiNames), WriterSanitizesNonAsciiNames);
        Run(nameof(ReadNameReadsOnlyHeader), ReadNameReadsOnlyHeader);
        Run(nameof(AtomicSaveCreatesAndReplaces), AtomicSaveCreatesAndReplaces);
        Run(nameof(ContentEqualsIgnoresEmptyCellPayload), ContentEqualsIgnoresEmptyCellPayload);
        Run(nameof(HistoryUndoRedo), HistoryUndoRedo);
        Run(nameof(HistoryIgnoresNoOps), HistoryIgnoresNoOps);
        Run(nameof(HistoryNewChangeClearsRedo), HistoryNewChangeClearsRedo);
        Run(nameof(HistoryGroupsMergeSteps), HistoryGroupsMergeSteps);
        Run(nameof(HistoryCancelGroupRestores), HistoryCancelGroupRestores);
        Run(nameof(HistoryJumpTo), HistoryJumpTo);
        Run(nameof(HistoryRespectsMaxDepth), HistoryRespectsMaxDepth);
        Run(nameof(HistoryRestoresCopies), HistoryRestoresCopies);
        Run(nameof(JsonRoundTrip), JsonRoundTrip);
        Run(nameof(JsonRejectsMalformed), JsonRejectsMalformed);
        Run(nameof(ExtrasRoundTripAndPreserveUnknown), ExtrasRoundTripAndPreserveUnknown);
        Run(nameof(ExtrasAreUndoableContent), ExtrasAreUndoableContent);
        Run(nameof(SidecarSaveLoadAndCleanup), SidecarSaveLoadAndCleanup);
        Run(nameof(DamagedSidecarIsReportedAndBackedUp), DamagedSidecarIsReportedAndBackedUp);
        RunConnectivityTests();
        RunSelectionTests();

        Console.WriteLine();
        Console.WriteLine($"{passed} passed, {failures.Count} failed");
        foreach (var failure in failures)
            Console.WriteLine("  FAILED " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("  ok   " + name);
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.Message}");
            Console.WriteLine("  FAIL " + name + ": " + ex.Message);
        }
    }

    // assertions
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new Exception($"expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
        }
        throw new Exception($"expected {typeof(T).Name}, nothing was thrown");
    }

    // helpers
    private static TdfDocument SampleDocument(string name = "Sample Track")
    {
        var doc = new TdfDocument(4, 3) { Name = name };
        doc.SetCell(0, 0, new TdfCell(1, 0, 0));    // start grid
        doc.SetCell(2, 1, new TdfCell(110, 3, 4));
        doc.SetCell(3, 2, new TdfCell(45, 1, 20));
        doc.SetPickup(2, 1, true);
        doc.SetPickup(3, 2, true);
        return doc;
    }

    private static TdfDocument ReadBytes(byte[] bytes) => TdfDocument.Read(new MemoryStream(bytes));

    /// <summary>
    /// The editor's original writer (EditorTrack.WriteBinary + BinaryExtensions.WritePaddedString
    /// before this refactor), reproduced to prove the new writer is compatible.
    /// </summary>
    private static byte[] LegacyWrite(string name, int width, int height, (ushort id, ushort rot, ushort elev)?[] cells, IEnumerable<(int x, int y)> pickups)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms)) // default: UTF-8
        {
            writer.Write(0x20464454);
            writer.Write((ushort)2);

            int len = Math.Min(byte.MaxValue, name.Length);
            writer.Write((byte)len);
            for (int i = 0; i < len; i++)
                writer.Write(name[i]);
            writer.Write('\x00');
            if ((len % 2) > 0)
                writer.Write('\x00');

            writer.Write((ushort)0);
            writer.Write((int)1);
            writer.Write((ushort)width);
            writer.Write((ushort)height);

            foreach (var cell in cells)
            {
                if (cell.HasValue)
                {
                    writer.Write(cell.Value.id);
                    writer.Write(cell.Value.rot);
                    writer.Write(cell.Value.elev);
                }
                else
                {
                    writer.Write((ushort)32767);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                }
            }

            var list = pickups.ToList();
            writer.Write((ushort)list.Count);
            foreach (var (x, y) in list)
            {
                writer.Write((ushort)x);
                writer.Write((ushort)y);
            }
            writer.Flush();
            return ms.ToArray();
        }
    }

    private static byte[] LegacyWrite(TdfDocument doc, string nameOverride = null)
    {
        var cells = doc.Cells.Select(c => c.HasModule ? ((ushort, ushort, ushort)?)(c.ModuleId, c.Rotation, c.ElevationSteps) : null).ToArray();
        var pickups = Enumerable.Range(0, doc.Pickups.Length).Where(i => doc.Pickups[i]).Select(i => (i % doc.Width, i / doc.Width));
        return LegacyWrite(nameOverride ?? doc.Name, doc.Width, doc.Height, cells, pickups);
    }

    // TDF tests
    private static void RoundTripPreservesContent()
    {
        var doc = SampleDocument();
        var read = ReadBytes(doc.ToBytes());
        Assert(doc.ContentEquals(read), "round trip changed content");
        Equal(2, read.PickupCount, "pickup count");
        Equal(3, read.ModuleCount, "module count");
        Equal((ushort)20, read.GetCell(3, 2).ElevationSteps, "elevation");
        Equal(0, read.Warnings.Count, "warnings");
    }

    private static void WriterMatchesLegacyWriterByteForByte()
    {
        // odd and even name lengths exercise the padding
        foreach (var name in new[] { "", "A", "AB", "Sample Track", "Odd name!" })
        {
            var doc = SampleDocument(name);
            var expected = LegacyWrite(doc);
            var actual = doc.ToBytes();
            Assert(expected.SequenceEqual(actual), $"bytes differ for name '{name}' ({expected.Length} vs {actual.Length})");
        }
    }

    private static void ReadsLegacyUtf8Names()
    {
        // the old writer emitted non-ASCII characters as UTF-8 while the length byte counted characters
        var doc = SampleDocument();
        var bytes = LegacyWrite(doc, "Café Ü");
        var read = ReadBytes(bytes);
        Equal("Café Ü", read.Name, "name");
        Assert(doc.Cells.SequenceEqual(read.Cells), "cells misaligned after UTF-8 name");
        Equal(2, read.PickupCount, "pickups");
    }

    private static void ReadsLatin1Names()
    {
        var doc = SampleDocument("Cafe");
        var bytes = doc.ToBytes();
        bytes[4 + 2 + 1 + 3] = 0xE9; // "Cafe" -> "Café" in Latin-1, same length
        var read = ReadBytes(bytes);
        Equal("Café", read.Name, "name");
        Assert(doc.Cells.SequenceEqual(read.Cells), "cells misaligned after Latin-1 name");
    }

    private static void ReadsVersion1WithoutPickups()
    {
        var doc = SampleDocument("v1");
        var bytes = doc.ToBytes();
        bytes[4] = 1; // version 1
        int pickupBlock = 2 + doc.PickupCount * 4;
        var v1 = bytes.Take(bytes.Length - pickupBlock).ToArray();
        var read = ReadBytes(v1);
        Equal((ushort)1, read.Version, "version");
        Equal(0, read.PickupCount, "pickups");
        Equal(3, read.ModuleCount, "modules");
    }

    private static void RejectsWrongMagic()
    {
        var bytes = SampleDocument().ToBytes();
        bytes[0] = (byte)'X';
        Throws<TdfFormatException>(() => ReadBytes(bytes));
    }

    private static void RejectsUnsupportedVersion()
    {
        var bytes = SampleDocument().ToBytes();
        bytes[4] = 3;
        var ex = Throws<TdfFormatException>(() => ReadBytes(bytes));
        Assert(ex.Message.Contains("version 3"), "message should name the version");
    }

    private static void RejectsTruncatedCells()
    {
        var bytes = SampleDocument().ToBytes();
        Throws<TdfFormatException>(() => ReadBytes(bytes.Take(30).ToArray()));
    }

    private static void RejectsHugeGridWithoutAllocating()
    {
        var bytes = SampleDocument("x").ToBytes();
        // header: magic(4) version(2) string(1+1+1+1 = len,x,pad,pad) theme(2) sections(4) => width at offset 16
        bytes[16] = 0xFF; bytes[17] = 0xFF;
        bytes[18] = 0xFF; bytes[19] = 0xFF;
        var ex = Throws<TdfFormatException>(() => ReadBytes(bytes));
        Assert(ex.Message.Contains("truncated"), "should report truncation: " + ex.Message);
    }

    private static void RejectsTruncatedPickups()
    {
        var bytes = SampleDocument().ToBytes();
        Throws<TdfFormatException>(() => ReadBytes(bytes.Take(bytes.Length - 2).ToArray()));
    }

    private static void WarnsAboutPickupsOutsideGrid()
    {
        var doc = SampleDocument();
        var cells = doc.Cells.Select(c => c.HasModule ? ((ushort, ushort, ushort)?)(c.ModuleId, c.Rotation, c.ElevationSteps) : null).ToArray();
        var bytes = LegacyWrite(doc.Name, doc.Width, doc.Height, cells, new[] { (1, 1), (40, 1), (1, 99) });
        var read = ReadBytes(bytes);
        Equal(1, read.PickupCount, "valid pickups");
        Equal(1, read.Warnings.Count, "warnings");
        Assert(read.Warnings[0].StartsWith("2 pickup"), read.Warnings[0]);
    }

    private static void ModuleSpaceAndNullAreEmpty()
    {
        Assert(!new TdfCell(TdfDocument.ModuleNull, 0, 0).HasModule, "null");
        Assert(!new TdfCell(TdfDocument.ModuleSpace, 2, 5).HasModule, "space");
        Assert(new TdfCell(1, 0, 0).HasModule, "start grid");

        // TWM_SPACE is written back as an empty cell, like the original loader treated it
        var doc = new TdfDocument(2, 1);
        doc.SetCell(0, 0, new TdfCell(TdfDocument.ModuleSpace, 1, 1));
        var read = ReadBytes(doc.ToBytes());
        Equal(TdfDocument.ModuleNull, read.GetCell(0, 0).ModuleId, "space cell");
    }

    private static void SanitizeNameKeepsPrintableAscii()
    {
        Equal("Hello World", TdfDocument.SanitizeName("  Hello World  "), "trim");
        Equal("Caf_", TdfDocument.SanitizeName("Café"), "non-ascii replaced");
        Equal("ab", TdfDocument.SanitizeName("a\tb"), "control removed");
        Equal(TdfDocument.MaxNameLength, TdfDocument.SanitizeName(new string('x', 100)).Length, "length");
        Equal(string.Empty, TdfDocument.SanitizeName(null), "null");
    }

    private static void WriterSanitizesNonAsciiNames()
    {
        var doc = SampleDocument("Ünïcode ✓");
        var read = ReadBytes(doc.ToBytes());
        Equal("_n_code _", read.Name, "name");
        Assert(doc.Cells.SequenceEqual(read.Cells), "cells");
    }

    private static void ReadNameReadsOnlyHeader()
    {
        string path = Path.Combine(Path.GetTempPath(), "tdf-test-" + Guid.NewGuid().ToString("N") + ".TDF");
        try
        {
            var bytes = SampleDocument("Header Only").ToBytes();
            File.WriteAllBytes(path, bytes.Take(20).ToArray()); // cells missing entirely
            Equal("Header Only", TdfDocument.ReadName(path), "name");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AtomicSaveCreatesAndReplaces()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tdf-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "!Track.TDF");
        try
        {
            SampleDocument("First").Save(path);
            Equal("First", TdfDocument.Load(path).Name, "created");

            SampleDocument("Second").Save(path);
            Equal("Second", TdfDocument.Load(path).Name, "replaced");

            var leftovers = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray();
            Equal(1, leftovers.Length, "files in folder: " + string.Join(", ", leftovers));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private static void ContentEqualsIgnoresEmptyCellPayload()
    {
        var a = new TdfDocument(2, 2);
        var b = new TdfDocument(2, 2);
        b.SetCell(1, 1, new TdfCell(TdfDocument.ModuleNull, 3, 7));
        Assert(a.ContentEquals(b), "empty cells with junk payload should be equal");
        b.SetPickup(0, 0, true);
        Assert(!a.ContentEquals(b), "pickup difference");
        var c = a.Clone();
        c.Name = "x";
        Assert(!a.ContentEquals(c), "name difference");
    }

    // Extras / sidecar tests
    private static void JsonRoundTrip()
    {
        var value = new Dictionary<string, object>
        {
            ["name"] = "quote \" and \\ and \n newline",
            ["n"] = 3.25,
            ["list"] = new List<object> { 1, true, null, "x" },
            ["nested"] = new Dictionary<string, object> { ["empty"] = new List<object>() }
        };
        var parsed = MiniJson.AsObject(MiniJson.Parse(MiniJson.Serialize(value)));
        Equal((string)value["name"], MiniJson.GetString(parsed, "name"), "string escapes");
        Equal(3.25, MiniJson.GetNumber(parsed, "n"), "number");
        var list = MiniJson.AsArray(parsed["list"]);
        Equal(4, list.Count, "list length");
        Equal(true, (bool)list[1], "bool");
        Assert(list[2] == null, "null");
        Equal(0, MiniJson.AsArray(MiniJson.AsObject(parsed["nested"])["empty"]).Count, "empty array");
    }

    private static void JsonRejectsMalformed()
    {
        Throws<MiniJson.ParseException>(() => MiniJson.Parse("{\"a\": }"));
        Throws<MiniJson.ParseException>(() => MiniJson.Parse("[1, 2"));
        Throws<MiniJson.ParseException>(() => MiniJson.Parse("{} trailing"));
    }

    private static void ExtrasRoundTripAndPreserveUnknown()
    {
        var extras = new TrackExtras();
        extras.Supports[(2, 1)] = new SupportStyle(0xFF8000, 0x102030);
        string json = extras.ToJson();

        // a newer editor added a section this version doesn't know
        var root = MiniJson.AsObject(MiniJson.Parse(json));
        root["raceData"] = new Dictionary<string, object> { ["aiNodes"] = new List<object> { 1, 2, 3 } };
        var warnings = new List<string>();
        var read = TrackExtras.FromJson(MiniJson.Serialize(root), warnings);

        Equal(0, warnings.Count, "warnings");
        Assert(read.Supports[(2, 1)].Equals(new SupportStyle(0xFF8000, 0x102030)), "support colors");
        string rewritten = read.ToJson();
        Assert(rewritten.Contains("raceData") && rewritten.Contains("aiNodes"), "unknown section kept on save");
        Assert(read.Clone().ContentEquals(read), "clone equal");
    }

    private static void ExtrasAreUndoableContent()
    {
        var a = SampleDocument();
        var b = a.Clone();
        Assert(a.ContentEquals(b), "clones equal");
        b.Extras.Supports[(2, 1)] = new SupportStyle(0x123456, 0);
        Assert(!a.ContentEquals(b), "support color difference detected");

        var t = new FakeTrack();
        t.History.Perform("color", () => t.State.Extras.Supports[(0, 0)] = new SupportStyle(1, 2));
        Equal(1, t.History.UndoEntries.Count, "recorded");
        t.History.Undo();
        Assert(t.State.Extras.Supports.Count == 0, "undo removes the color");
    }

    private static void SidecarSaveLoadAndCleanup()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tdf-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "!Track.TDF");
        try
        {
            var doc = SampleDocument();
            doc.Extras.Supports[(2, 1)] = new SupportStyle(0xAABBCC, 0x112233);
            Directory.CreateDirectory(dir);
            TrackFiles.Save(doc, path);
            Assert(File.Exists(TrackFiles.ExtrasPath(path)), "sidecar written");
            Assert(TrackFiles.ExtrasPath(path).EndsWith("!Track.tdx"), "sidecar name");

            var loaded = TrackFiles.Load(path);
            Assert(doc.ContentEquals(loaded), "round trip including extras");

            // the TDF itself is unchanged by extras: byte-identical to a plain save
            Assert(File.ReadAllBytes(path).SequenceEqual(SampleDocument().ToBytes()), "TDF bytes unaffected by extras");

            loaded.Extras.Supports.Clear();
            TrackFiles.Save(loaded, path);
            Assert(!File.Exists(TrackFiles.ExtrasPath(path)), "empty extras remove the sidecar");

            TrackFiles.Delete(path);
            Equal(0, Directory.GetFiles(dir).Length, "delete removes both files");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private static void DamagedSidecarIsReportedAndBackedUp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tdf-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "!Track.TDF");
        try
        {
            Directory.CreateDirectory(dir);
            TrackFiles.Save(SampleDocument(), path);
            File.WriteAllText(TrackFiles.ExtrasPath(path), "{ this is not json");

            var loaded = TrackFiles.Load(path);
            Equal(3, loaded.ModuleCount, "track still loads");
            Assert(loaded.Warnings.Any(w => w.Contains("couldn't be read")), "damage reported");

            TrackFiles.Save(loaded, path);
            Assert(File.Exists(TrackFiles.ExtrasPath(path) + ".bak"), "damaged sidecar kept as .bak");
            Equal("{ this is not json", File.ReadAllText(TrackFiles.ExtrasPath(path) + ".bak"), "backup content");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    // History tests: the "track" is just a document that the history captures and restores
    private sealed class FakeTrack
    {
        public TdfDocument State = new TdfDocument(3, 3);
        public int Restores;
        public readonly TrackHistory History;

        public FakeTrack()
        {
            History = new TrackHistory(() => State.Clone(), doc => { State = doc; Restores++; });
        }

        public void Place(int x, int y, ushort id) => History.Perform($"place {id}", () => State.SetCell(x, y, new TdfCell(id, 0, 0)));
        public ushort At(int x, int y) => State.GetCell(x, y).ModuleId;
    }

    private static void HistoryUndoRedo()
    {
        var t = new FakeTrack();
        t.Place(0, 0, 5);
        t.Place(1, 0, 6);
        Equal("place 6", t.History.UndoLabel, "undo label");

        Assert(t.History.Undo(), "undo 1");
        Equal(TdfDocument.ModuleNull, t.At(1, 0), "after undo");
        Equal((ushort)5, t.At(0, 0), "first placement kept");
        Equal("place 6", t.History.RedoLabel, "redo label");

        Assert(t.History.Redo(), "redo");
        Equal((ushort)6, t.At(1, 0), "after redo");

        t.History.Undo();
        t.History.Undo();
        Assert(!t.History.CanUndo, "nothing left to undo");
        Assert(!t.History.Undo(), "undo past start");
        Equal(TdfDocument.ModuleNull, t.At(0, 0), "back to start");
    }

    private static void HistoryIgnoresNoOps()
    {
        var t = new FakeTrack();
        t.Place(0, 0, 5);
        bool changed = t.History.Perform("again", () => t.State.SetCell(0, 0, new TdfCell(5, 0, 0)));
        Assert(!changed, "no-op reported as change");
        Equal(1, t.History.UndoEntries.Count, "entries");
    }

    private static void HistoryNewChangeClearsRedo()
    {
        var t = new FakeTrack();
        t.Place(0, 0, 5);
        t.History.Undo();
        Assert(t.History.CanRedo, "can redo");
        t.Place(2, 2, 9);
        Assert(!t.History.CanRedo, "redo cleared");
    }

    private static void HistoryGroupsMergeSteps()
    {
        var t = new FakeTrack();
        t.History.BeginGroup("paint");
        t.Place(0, 0, 1);
        t.Place(1, 0, 1);
        t.Place(2, 0, 1);
        Assert(!t.History.CanUndo, "undo disabled while grouping");
        Assert(t.History.EndGroup(), "group changed track");
        Equal(1, t.History.UndoEntries.Count, "one entry");
        Equal("paint", t.History.UndoLabel, "label");

        t.History.Undo();
        Equal(TdfDocument.ModuleNull, t.At(2, 0), "whole group undone");
        Equal(TdfDocument.ModuleNull, t.At(0, 0), "whole group undone");

        t.History.BeginGroup("empty");
        Assert(!t.History.EndGroup(), "empty group not recorded");
    }

    private static void HistoryCancelGroupRestores()
    {
        var t = new FakeTrack();
        t.Place(0, 0, 3);
        t.History.BeginGroup("drag");
        t.Place(1, 1, 4);
        t.History.CancelGroup();
        Equal(TdfDocument.ModuleNull, t.At(1, 1), "cancelled change reverted");
        Equal((ushort)3, t.At(0, 0), "earlier change kept");
        Equal(1, t.History.UndoEntries.Count, "entries");
    }

    private static void HistoryJumpTo()
    {
        var t = new FakeTrack();
        for (ushort i = 1; i <= 5; i++)
            t.Place(i % 3, i / 3, i);

        int restoresBefore = t.Restores;
        t.History.JumpTo(1);
        Equal(restoresBefore + 1, t.Restores, "single restore for a multi-step jump");
        Equal(1, t.History.UndoEntries.Count, "undo entries");
        Equal(4, t.History.RedoEntries.Count, "redo entries");
        Equal((ushort)1, t.At(1, 0), "first placement present");
        Equal(TdfDocument.ModuleNull, t.At(2, 0), "second placement gone");

        t.History.JumpTo(5);
        Equal((ushort)5, t.At(2, 1), "all redone");
        Equal(0, t.History.RedoEntries.Count, "redo empty");

        // step-by-step undo must agree with the jump
        t.History.Undo();
        t.History.Undo();
        var stepped = t.State.Clone();
        t.History.JumpTo(5);
        t.History.JumpTo(3);
        Assert(stepped.ContentEquals(t.State), "jump and step disagree");
    }

    private static void HistoryRespectsMaxDepth()
    {
        var t = new FakeTrack();
        t.History.MaxDepth = 3;
        for (ushort i = 1; i <= 6; i++)
            t.Place(0, 0, i);
        Equal(3, t.History.UndoEntries.Count, "depth");
        t.History.JumpTo(0);
        Equal((ushort)3, t.At(0, 0), "oldest reachable state");
    }

    private static void HistoryRestoresCopies()
    {
        // mutating the live state after undo must not corrupt the stored redo snapshot
        var t = new FakeTrack();
        t.Place(0, 0, 7);
        t.History.Undo();
        t.State.SetCell(2, 2, new TdfCell(99, 0, 0)); // not recorded on purpose
        t.History.Redo();
        Equal((ushort)7, t.At(0, 0), "redo state");
        t.History.Undo();
        Equal((ushort)99, t.At(2, 2), "live edit preserved in redo snapshot capture");
    }
}
