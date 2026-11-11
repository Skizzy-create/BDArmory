using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using BDArmory.Utils;

namespace OverdriveTests
{
    public static class PerfProbeTests
    {
        // ---- section metadata ----
        [Test] public static void Sections_NamesMatchEnum() => Assert.Equal(Enum.GetValues(typeof(PerfSection)).Length, PerfSections.Names.Length);
        [Test] public static void Sections_TopLevelMatchesEnum() => Assert.Equal(PerfSections.Count, PerfSections.TopLevel.Length);

        [Test]
        public static void Sections_NamesUniqueAndCsvSafe()
        {
            var seen = new HashSet<string>();
            foreach (var name in PerfSections.Names)
            {
                Assert.True(seen.Add(name), $"duplicate section name {name}");
                Assert.False(name.Contains(',') || name.Contains(' ') || name.Contains('"'), $"unsafe CSV name {name}");
            }
        }

        [Test]
        public static void Sections_EntryPointsAreTopLevelAndHotFunctionsNested()
        {
            Assert.True(PerfSections.TopLevel[(int)PerfSection.WeaponManagerFixed]);
            Assert.True(PerfSections.TopLevel[(int)PerfSection.Bullets]);
            Assert.True(PerfSections.TopLevel[(int)PerfSection.SetupGUI]);
            Assert.True(PerfSections.TopLevel[(int)PerfSection.TargetManager]);
            Assert.False(PerfSections.TopLevel[(int)PerfSection.GuardScan]);
            Assert.False(PerfSections.TopLevel[(int)PerfSection.TargetSelection]);
            Assert.False(PerfSections.TopLevel[(int)PerfSection.HeatTarget]);
            Assert.False(PerfSections.TopLevel[(int)PerfSection.CanSeeTarget]);
        }

        // ---- accumulator ----
        [Test]
        public static void Accumulator_TopLevelCountsTowardsTotal()
        {
            var acc = new PerfAccumulator();
            long t = acc.Begin(PerfSection.WeaponManagerFixed, 100);
            acc.End(PerfSection.WeaponManagerFixed, t, 150);
            Assert.Equal(50L, acc.SectionTicks(PerfSection.WeaponManagerFixed));
            Assert.Equal(1, acc.SectionCalls(PerfSection.WeaponManagerFixed));
            Assert.Equal(50L, acc.BdaTicks);
            Assert.Equal(0, acc.TopDepth);
        }

        [Test]
        public static void Accumulator_NestedSectionDoesNotAddToTotal()
        {
            var acc = new PerfAccumulator();
            long outer = acc.Begin(PerfSection.WeaponManagerFixed, 0);
            long inner = acc.Begin(PerfSection.GuardScan, 10);
            acc.End(PerfSection.GuardScan, inner, 40);
            acc.End(PerfSection.WeaponManagerFixed, outer, 100);
            Assert.Equal(30L, acc.SectionTicks(PerfSection.GuardScan));
            Assert.Equal(100L, acc.SectionTicks(PerfSection.WeaponManagerFixed));
            Assert.Equal(100L, acc.BdaTicks, "the total is the outer entry point only");
        }

        [Test]
        public static void Accumulator_NestedEntryPointNotDoubleCounted()
        {
            var acc = new PerfAccumulator();
            long outer = acc.Begin(PerfSection.PilotAIFixed, 0);
            long inner = acc.Begin(PerfSection.WeaponManagerFixed, 20); // An entry point called from inside another.
            acc.End(PerfSection.WeaponManagerFixed, inner, 70);
            acc.End(PerfSection.PilotAIFixed, outer, 100);
            Assert.Equal(50L, acc.SectionTicks(PerfSection.WeaponManagerFixed));
            Assert.Equal(100L, acc.SectionTicks(PerfSection.PilotAIFixed));
            Assert.Equal(100L, acc.BdaTicks);
        }

        [Test]
        public static void Accumulator_SequentialEntryPointsSum()
        {
            var acc = new PerfAccumulator();
            for (int i = 0; i < 10; ++i)
            {
                long t = acc.Begin(PerfSection.MissileFixed, i * 100);
                acc.End(PerfSection.MissileFixed, t, i * 100 + 7);
            }
            Assert.Equal(70L, acc.SectionTicks(PerfSection.MissileFixed));
            Assert.Equal(10, acc.SectionCalls(PerfSection.MissileFixed));
            Assert.Equal(70L, acc.BdaTicks);
        }

        [Test]
        public static void Accumulator_ResetClearsValues()
        {
            var acc = new PerfAccumulator();
            long t = acc.Begin(PerfSection.Bullets, 0);
            acc.End(PerfSection.Bullets, t, 10);
            acc.Reset(20);
            Assert.Equal(0L, acc.SectionTicks(PerfSection.Bullets));
            Assert.Equal(0, acc.SectionCalls(PerfSection.Bullets));
            Assert.Equal(0L, acc.BdaTicks);
        }

        [Test]
        public static void Accumulator_ResetInsideOpenSectionCarriesRemainderToNextFrame()
        {
            var acc = new PerfAccumulator();
            long t = acc.Begin(PerfSection.SetupGUI, 0);
            acc.Reset(30); // Frame boundary while the section is open.
            acc.End(PerfSection.SetupGUI, t, 50);
            Assert.Equal(20L, acc.BdaTicks, "only the time after the boundary goes to the new frame's total");
            Assert.Equal(0, acc.TopDepth);
        }

        [Test]
        public static void Accumulator_UnmatchedEndDoesNotUnderflow()
        {
            var acc = new PerfAccumulator();
            acc.End(PerfSection.WeaponManagerFixed, 0, 10); // e.g., recording started inside a section.
            Assert.Equal(0, acc.TopDepth);
            Assert.Equal(0L, acc.BdaTicks);
            long t = acc.Begin(PerfSection.WeaponManagerFixed, 20);
            acc.End(PerfSection.WeaponManagerFixed, t, 25);
            Assert.Equal(5L, acc.BdaTicks);
        }

        [Test]
        public static void Accumulator_ResetDepthForgetsOpenSections()
        {
            var acc = new PerfAccumulator();
            acc.Begin(PerfSection.WeaponManagerFixed, 0);
            acc.ResetDepth();
            Assert.Equal(0, acc.TopDepth);
        }

        // ---- probe on/off ----
        [Test]
        public static void Probe_ScopeIsInertWhenDisabled()
        {
            bool was = PerfProbe.Enabled;
            try
            {
                PerfProbe.Enabled = false;
                PerfProbe.Accumulator.Reset(PerfProbe.Now());
                using (PerfProbe.Scope(PerfSection.RadarFixed)) { }
                Assert.Equal(0, PerfProbe.Accumulator.SectionCalls(PerfSection.RadarFixed));
                Assert.Equal(0L, PerfProbe.Accumulator.BdaTicks);
            }
            finally { PerfProbe.Enabled = was; }
        }

        [Test]
        public static void Probe_ScopeRecordsWhenEnabled()
        {
            bool was = PerfProbe.Enabled;
            try
            {
                PerfProbe.Enabled = true;
                PerfProbe.Accumulator.ResetDepth();
                PerfProbe.Accumulator.Reset(PerfProbe.Now());
                using (PerfProbe.Scope(PerfSection.RadarFixed))
                {
                    using (PerfProbe.Scope(PerfSection.RadarScanLock)) { Thread.SpinWait(1000); }
                }
                Assert.Equal(1, PerfProbe.Accumulator.SectionCalls(PerfSection.RadarFixed));
                Assert.Equal(1, PerfProbe.Accumulator.SectionCalls(PerfSection.RadarScanLock));
                Assert.True(PerfProbe.Accumulator.SectionTicks(PerfSection.RadarFixed) >= PerfProbe.Accumulator.SectionTicks(PerfSection.RadarScanLock), "outer includes inner");
                Assert.Equal(PerfProbe.Accumulator.SectionTicks(PerfSection.RadarFixed), PerfProbe.Accumulator.BdaTicks);
                Assert.Equal(0, PerfProbe.Accumulator.TopDepth);
            }
            finally
            {
                PerfProbe.Enabled = was;
                PerfProbe.Accumulator.ResetDepth();
                PerfProbe.Accumulator.Reset(PerfProbe.Now());
            }
        }

        [Test]
        public static void Probe_ScopeClosesOnException()
        {
            bool was = PerfProbe.Enabled;
            try
            {
                PerfProbe.Enabled = true;
                PerfProbe.Accumulator.ResetDepth();
                PerfProbe.Accumulator.Reset(PerfProbe.Now());
                try
                {
                    using var _ = PerfProbe.Scope(PerfSection.WeaponFixed);
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) { }
                Assert.Equal(0, PerfProbe.Accumulator.TopDepth);
                Assert.Equal(1, PerfProbe.Accumulator.SectionCalls(PerfSection.WeaponFixed));
            }
            finally
            {
                PerfProbe.Enabled = was;
                PerfProbe.Accumulator.ResetDepth();
                PerfProbe.Accumulator.Reset(PerfProbe.Now());
            }
        }

        [Test]
        public static void Probe_DisablingInsideScopeIsSafe()
        {
            bool was = PerfProbe.Enabled;
            try
            {
                PerfProbe.Enabled = true;
                PerfProbe.Accumulator.ResetDepth();
                var scope = PerfProbe.Scope(PerfSection.WeaponFixed);
                PerfProbe.Enabled = false; // Recording stopped (scene change) inside the section.
                PerfProbe.Accumulator.ResetDepth();
                scope.Dispose();
                Assert.Equal(0, PerfProbe.Accumulator.TopDepth);
            }
            finally
            {
                PerfProbe.Enabled = was;
                PerfProbe.Accumulator.ResetDepth();
                PerfProbe.Accumulator.Reset(PerfProbe.Now());
            }
        }

        // ---- CSV ----
        static PerfFrameStats SampleStats() => new()
        {
            frameIndex = 42,
            gameTime = 12.5,
            realTime = 3.25,
            frameMs = 100,
            unscaledDeltaMs = 99.5,
            fixedSteps = 2,
            fixedScriptsMs = 40,
            physicsMs = 30,
            updateMs = 10,
            gcCount = 1,
            heapMb = 2048.25,
            vesselsLoaded = 120,
            partsTotal = 2300,
            partsMaxVessel = 150,
            missilesInFlight = 100,
            bulletsActive = 800,
            guardModeWMs = 20,
            competitionActive = true,
            probeMs = 0.05,
        };

        static int FieldCount(string line) => line.Split(',').Length;

        [Test]
        public static void Csv_HeaderHasFrameAndSectionColumns()
        {
            var header = PerfCsv.Header();
            Assert.Equal(PerfCsv.FrameColumns.Length + 2 * PerfSections.Count, FieldCount(header));
            Assert.True(header.StartsWith("frame,game_time,real_time,frame_ms"), header);
            Assert.True(header.Contains(",bda_ms,non_bda_ms,"), "BDArmory vs everything else");
            Assert.True(header.Contains(",fixed_steps,") && header.Contains(",physics_ms,") && header.Contains(",parts_total,parts_max_vessel,"), "phase and part columns");
            Assert.True(header.Contains(",guard_scan_ms,guard_scan_n"), "nested section columns");
        }

        [Test]
        public static void Csv_HeaderColumnsUnique()
        {
            var seen = new HashSet<string>();
            foreach (var column in PerfCsv.Header().Split(',')) Assert.True(seen.Add(column), $"duplicate column {column}");
        }

        [Test]
        public static void Csv_RowMatchesHeaderAndValues()
        {
            var acc = new PerfAccumulator();
            long t = acc.Begin(PerfSection.WeaponManagerFixed, 0);
            acc.End(PerfSection.WeaponManagerFixed, t, 2500); // 2.5 ms at 1 tick = 1 µs.
            var sb = new StringBuilder();
            PerfCsv.AppendRow(sb, SampleStats(), acc, 0.001);
            var fields = sb.ToString().Split(',');
            var columns = PerfCsv.Header().Split(',');
            Assert.Equal(columns.Length, fields.Length);
            string Field(string name) => fields[Array.IndexOf(columns, name)];
            Assert.Equal("42", Field("frame"));
            Assert.Equal("100.000", Field("frame_ms"));
            Assert.Equal("2", Field("fixed_steps"));
            Assert.Equal("20.000", Field("other_ms"));
            Assert.Equal("2.500", Field("bda_ms"));
            Assert.Equal("97.500", Field("non_bda_ms"));
            Assert.Equal("2300", Field("parts_total"));
            Assert.Equal("150", Field("parts_max_vessel"));
            Assert.Equal("1", Field("competition_active"));
            Assert.Equal("2.500", Field("wm_fixed_ms"));
            Assert.Equal("1", Field("wm_fixed_n"));
            Assert.Equal("0.000", Field("guard_scan_ms"));
            Assert.Equal("0", Field("guard_scan_n"));
        }

        [Test]
        public static void Csv_InvariantCultureUnderCommaDecimalLocale()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var sb = new StringBuilder();
                PerfCsv.AppendRow(sb, SampleStats(), new PerfAccumulator(), 0.001);
                Assert.Equal(PerfCsv.Header().Split(',').Length, sb.ToString().Split(',').Length, "decimal commas would add fields");
                Assert.True(sb.ToString().Contains("2048.3") || sb.ToString().Contains("2048.2"), "decimal point");
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        [Test]
        public static void Csv_OtherMsNeverNegative()
        {
            var stats = SampleStats();
            stats.frameMs = 10; // Phases exceed the frame (clock granularity, frame boundary inside a phase).
            Assert.Near(0.0, PerfCsv.OtherMs(stats));
            Assert.Near(0.0, PerfCsv.NonBdaMs(stats, 12));
            stats.frameMs = 100;
            Assert.Near(20.0, PerfCsv.OtherMs(stats));
            Assert.Near(60.0, PerfCsv.NonBdaMs(stats, 40));
        }
    }
}
