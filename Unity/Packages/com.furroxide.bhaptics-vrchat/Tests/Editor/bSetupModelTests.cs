using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace bHapticsOSC.VRChat.Tests
{
    /// <summary>
    /// Covers the panel's presentation logic, which had none before: the wording used to be
    /// written inline at each draw site, so the only way to check that a state said the right
    /// thing was to reproduce that state in a live editor.
    ///
    /// The group and step builders read Application.platform and the package list, so these run
    /// in the EditMode runner like the rest of the suite. The assertions are written to hold on
    /// any host: nothing here depends on which packages happen to be installed.
    /// </summary>
    public class bSetupModelTests
    {
        private const string Required = "2.4.0";

        // ------------------------------------------------------------------ companion states

        [TestCase("ReadyRunning", "Ok")]
        [TestCase("ReadyStopped", "Attention")]
        [TestCase("RunningUninspectable", "Attention")]
        [TestCase("UnknownVersion", "Attention")]
        [TestCase("NotLocated", "Blocked")]
        [TestCase("MissingPath", "Blocked")]
        [TestCase("InvalidProduct", "Blocked")]
        [TestCase("ForeignBuild", "Blocked")]
        [TestCase("Outdated", "Blocked")]
        [TestCase("UnsupportedPlatform", "Unknown")]
        public void DescribeCompanion_MapsEveryStatusToItsUrgency(string status, string expected)
        {
            bSetupStep step = bSetupModel.DescribeCompanion(Companion(Status(status)));

            Assert.That(step.State, Is.EqualTo(State(expected)));
        }

        /// <summary>
        /// Every state has to explain itself. The panel shows Explanation on hover and behind the
        /// disclosure, so an empty one is a state the user cannot find out anything about.
        /// </summary>
        [Test]
        public void DescribeCompanion_EveryStatusExplainsItself()
        {
            foreach (bCompanionStatus status in Enum.GetValues(typeof(bCompanionStatus)))
            {
                bSetupStep step = bSetupModel.DescribeCompanion(Companion(status));

                Assert.That(step.Title, Is.Not.Empty, "Title missing for " + status);
                Assert.That(step.Explanation, Is.Not.Empty, "Explanation missing for " + status);
            }
        }

        /// <summary>
        /// A row that needs something must say what, in one short sentence, and offer a way to do
        /// it - that pair is what replaced the undifferentiated paragraph.
        /// </summary>
        [Test]
        public void DescribeCompanion_UnhappyStatusesOfferADetailAndAnAction()
        {
            foreach (bCompanionStatus status in Enum.GetValues(typeof(bCompanionStatus)))
            {
                bSetupStep step = bSetupModel.DescribeCompanion(Companion(status));
                if (!step.NeedsAttention)
                    continue;

                Assert.That(step.Detail, Is.Not.Empty, "Detail missing for " + status);
                Assert.That(step.Detail.Length, Is.LessThanOrEqualTo(80), "Detail too long for " + status);
                Assert.That(step.Actions, Is.Not.Empty, "No action offered for " + status);
            }
        }

        /// <summary>A satisfied row shows its Value and nothing else, so it must have one.</summary>
        [Test]
        public void DescribeCompanion_SatisfiedStatusesCarryAValue()
        {
            foreach (bCompanionStatus status in Enum.GetValues(typeof(bCompanionStatus)))
            {
                bSetupStep step = bSetupModel.DescribeCompanion(Companion(status));
                if (step.State != bStepState.Ok)
                    continue;

                Assert.That(step.Value, Is.Not.Empty, "Value missing for " + status);
            }
        }

        [Test]
        public void DescribeCompanion_ForeignBuild_SaysReplaceNotUpdate()
        {
            bSetupStep step = bSetupModel.DescribeCompanion(Companion(
                bCompanionStatus.ForeignBuild,
                detectedVersion: "2.2.1",
                productName: "bHaptics OSC for VRChat",
                lineage: bCompanionBuildLineage.Foreign));

            // Its version number is not comparable to this fork's, so "update it" is the wrong
            // advice and the one users reach for by default.
            Assert.That(step.Explanation, Does.Contain("Replace it"));
            Assert.That(step.Explanation, Does.Contain("bHaptics OSC for VRChat"));
        }

        [Test]
        public void DescribeCompanion_ForeignBuildRunning_SaysToStopItFirst()
        {
            bSetupStep step = bSetupModel.DescribeCompanion(Companion(
                bCompanionStatus.ForeignBuild,
                isRunning: true,
                lineage: bCompanionBuildLineage.Foreign));

            Assert.That(step.Explanation, Does.Contain("OSC port"));
        }

        // ------------------------------------------------------------------ groups

        [Test]
        public void Build_OrdersGroupsByUrgency()
        {
            IReadOnlyList<bSetupGroup> groups = Build(Companion(bCompanionStatus.ReadyRunning), Environment());

            // The per-session PC checks used to be drawn last, below the fold of a 560px window,
            // while the near-static package list came first.
            Assert.That(groups[0].Id, Is.EqualTo(bSetupModel.GroupPc));
            Assert.That(groups[1].Id, Is.EqualTo(bSetupModel.GroupAvatar));
            Assert.That(groups[2].Id, Is.EqualTo(bSetupModel.GroupProject));
        }

        [Test]
        public void Build_ConflictingProcess_BecomesItsOwnBlockingStep()
        {
            bCompanionStatusResult conflicted = Companion(bCompanionStatus.ReadyRunning)
                .WithConflictingProcess("bHapticsOSC_v2.2.1");

            bSetupGroup pc = Build(conflicted, Environment())[0];

            bSetupStep conflict = FindStep(pc, bSetupModel.StepConflict);
            Assert.That(conflict.State, Is.EqualTo(bStepState.Blocked));
            Assert.That(conflict.Detail, Does.Contain("bHapticsOSC_v2.2.1"));
        }

        [Test]
        public void Build_WithoutAConflict_OmitsTheConflictStep()
        {
            bSetupGroup pc = Build(Companion(bCompanionStatus.ReadyRunning), Environment())[0];

            Assert.That(HasStep(pc, bSetupModel.StepConflict), Is.False);
        }

        /// <summary>
        /// Seeing this package's parameters on an avatar VRChat has loaded is the only proof
        /// inside Unity that the whole chain works, so it gets its own row rather than being
        /// appended as a second paragraph to the OSC row.
        /// </summary>
        [Test]
        public void Build_HapticAvatarSeen_IsPromotedToItsOwnStep()
        {
            bEnvironment environment = Environment(
                oscEnabled: bProbeState.Yes,
                hapticAvatarName: "Test Avatar");

            bSetupGroup pc = Build(Companion(bCompanionStatus.ReadyRunning), environment)[0];

            if (!IsWindowsEditor(pc))
                Assert.Ignore("The player and OSC probes only run in a Windows editor.");

            bSetupStep chain = FindStep(pc, bSetupModel.StepChain);
            Assert.That(chain.State, Is.EqualTo(bStepState.Ok));
            Assert.That(chain.Value, Is.EqualTo("Test Avatar"));
        }

        [Test]
        public void Build_NoHapticAvatarYet_KeepsTheEvidenceOnTheOscStep()
        {
            bEnvironment environment = Environment(
                oscEnabled: bProbeState.Yes,
                oscConfigWritten: new DateTime(2026, 8, 1));

            bSetupGroup pc = Build(Companion(bCompanionStatus.ReadyRunning), environment)[0];

            if (!IsWindowsEditor(pc))
                Assert.Ignore("The player and OSC probes only run in a Windows editor.");

            Assert.That(HasStep(pc, bSetupModel.StepChain), Is.False);
            // Naming a month here is only safe because AppendConfigEvidence pins its format to the
            // invariant culture. Were that left to the host's culture, this would fail on a French or
            // Japanese editor over the spelling of one word, which has nothing to do with the
            // behaviour under test - so if this line ever starts failing, check that pin first.
            Assert.That(FindStep(pc, bSetupModel.StepOsc).Explanation, Does.Contain("Aug 2026"));
        }

        [TestCase("Yes", "Yes", "Ok")]
        [TestCase("Unknown", "Yes", "Ok")]
        [TestCase("Yes", "No", "Attention")]
        [TestCase("No", "No", "Blocked")]
        [TestCase("No", "Unknown", "Blocked")]
        [TestCase("Yes", "Unknown", "Unknown")]
        [TestCase("Unknown", "No", "Unknown")]
        [TestCase("Unknown", "Unknown", "Unknown")]
        public void Build_PlayerStep_ReflectsWhatWasProbed(
            string installed,
            string running,
            string expected)
        {
            bSetupGroup pc = Build(
                Companion(bCompanionStatus.ReadyRunning),
                Environment(playerInstalled: Probe(installed), playerRunning: Probe(running)))[0];

            if (!IsWindowsEditor(pc))
                Assert.Ignore("The player and OSC probes only run in a Windows editor.");

            Assert.That(FindStep(pc, bSetupModel.StepPlayer).State, Is.EqualTo(State(expected)));
        }

        /// <summary>
        /// An indeterminate probe is not a negative one. Reading the install path can throw and
        /// enumerating processes can be refused, and in both cases the Player may be sitting there
        /// working - so the row may assert neither that it is missing nor that it is stopped, and
        /// it must not be counted among the problems holding the user up.
        /// </summary>
        [Test]
        public void Build_PlayerStep_IndeterminateProbes_DoNotAssertItIsMissingOrStopped()
        {
            bSetupGroup pc = Build(
                Companion(bCompanionStatus.ReadyRunning),
                Environment(
                    playerInstalled: bProbeState.Unknown,
                    playerRunning: bProbeState.Unknown))[0];

            if (!IsWindowsEditor(pc))
                Assert.Ignore("The player and OSC probes only run in a Windows editor.");

            bSetupStep player = FindStep(pc, bSetupModel.StepPlayer);
            Assert.That(player.Detail, Does.Not.Contain("Not found"));
            Assert.That(player.Detail, Does.Not.Contain("not running"));
            Assert.That(player.NeedsAttention, Is.False);
        }

        [TestCase("Yes", "Ok")]
        [TestCase("No", "Blocked")]
        [TestCase("Unknown", "Unknown")]
        public void Build_OscStep_ReflectsWhatWasProbed(string osc, string expected)
        {
            bSetupGroup pc = Build(
                Companion(bCompanionStatus.ReadyRunning),
                Environment(oscEnabled: Probe(osc)))[0];

            if (!IsWindowsEditor(pc))
                Assert.Ignore("The player and OSC probes only run in a Windows editor.");

            Assert.That(FindStep(pc, bSetupModel.StepOsc).State, Is.EqualTo(State(expected)));
        }

        // ------------------------------------------------------------------ roll-ups

        [Test]
        public void FirstActionable_PrefersABlockerOverSomethingMerelyPending()
        {
            var groups = new[]
            {
                Group("a", Step("pending", bStepState.Attention)),
                Group("b", Step("broken", bStepState.Blocked)),
            };

            Assert.That(bSetupModel.FirstActionable(groups)?.Id, Is.EqualTo("broken"));
        }

        [Test]
        public void FirstActionable_FallsBackToTheFirstPendingStep()
        {
            var groups = new[]
            {
                Group("a", Step("fine", bStepState.Ok), Step("pending", bStepState.Attention)),
                Group("b", Step("later", bStepState.Attention)),
            };

            Assert.That(bSetupModel.FirstActionable(groups)?.Id, Is.EqualTo("pending"));
        }

        /// <summary>
        /// Something that could not be checked is the last thing the banner falls back to, never
        /// something it leads with while a real problem is waiting.
        /// </summary>
        [Test]
        public void FirstActionable_PrefersRealProblemsOverSomethingUnchecked()
        {
            var groups = new[]
            {
                Group("a", Step("unknowable", bStepState.Unknown)),
                Group("b", Step("pending", bStepState.Attention)),
                Group("c", Step("broken", bStepState.Blocked)),
            };

            Assert.That(bSetupModel.FirstActionable(groups)?.Id, Is.EqualTo("broken"));

            var withoutTheBlocker = new[]
            {
                Group("a", Step("unknowable", bStepState.Unknown)),
                Group("b", Step("pending", bStepState.Attention)),
            };

            Assert.That(bSetupModel.FirstActionable(withoutTheBlocker)?.Id, Is.EqualTo("pending"));
        }

        /// <summary>
        /// With nothing left to do the banner still has to say something honest, and "everything is
        /// ready" is not available over a probe that came back empty.
        /// </summary>
        [Test]
        public void FirstActionable_FallsBackToTheFirstUncheckedStep()
        {
            var groups = new[] { Group("a", Step("fine", bStepState.Ok), Step("unknowable", bStepState.Unknown)) };

            Assert.That(bSetupModel.FirstActionable(groups)?.Id, Is.EqualTo("unknowable"));
            Assert.That(bSetupModel.FirstActionable(groups)?.State, Is.EqualTo(bStepState.Unknown));
        }

        [Test]
        public void FirstActionable_EverythingCheckedAndPassing_IsNull()
        {
            var groups = new[] { Group("a", Step("fine", bStepState.Ok), Step("also-fine", bStepState.Ok)) };

            Assert.That(bSetupModel.FirstActionable(groups), Is.Null);
        }

        [TestCase("Ok", "Ok", "Ready to play")]
        [TestCase("Attention", "Ok", "1 thing to do")]
        [TestCase("Attention", "Attention", "2 things to do")]
        [TestCase("Blocked", "Ok", "1 problem")]
        [TestCase("Blocked", "Blocked", "2 problems")]
        [TestCase("Blocked", "Attention", "1 problem")]
        [TestCase("Ok", "Unknown", "1 not checked")]
        [TestCase("Unknown", "Unknown", "2 not checked")]
        [TestCase("Attention", "Unknown", "1 thing to do")]
        [TestCase("Blocked", "Unknown", "1 problem")]
        public void DescribeOverall_ReportsTheWorstThingItFound(string first, string second, string expected)
        {
            var groups = new[] { Group("a", Step("one", State(first)), Step("two", State(second))) };

            Assert.That(bSetupModel.DescribeOverall(groups), Is.EqualTo(expected));
        }

        /// <summary>
        /// A clean group folds itself shut behind an "all set" header, so clean has to mean every
        /// step in it was checked and passed. Hiding a step whose probe came back empty would show
        /// a check that never ran as one that did.
        /// </summary>
        [Test]
        public void Group_IsCleanOnlyWhenEveryStepWasCheckedAndPassed()
        {
            Assert.That(Group("a", Step("one", bStepState.Ok), Step("two", bStepState.Ok)).IsClean, Is.True);
            Assert.That(Group("a", Step("one", bStepState.Ok), Step("two", bStepState.Unknown)).IsClean, Is.False);
            Assert.That(Group("a", Step("one", bStepState.Ok), Step("two", bStepState.Attention)).IsClean, Is.False);
            Assert.That(Group("a", Step("one", bStepState.Ok), Step("two", bStepState.Blocked)).IsClean, Is.False);
        }

        /// <summary>
        /// Unknown sits between the two predicates rather than under either: it asks nothing of the
        /// user, so the row stays a quiet single line, but it was never verified, so nothing may
        /// count it as a pass. Collapsing these back into one predicate is what the panel used to
        /// do, and it is what let a group of unread checks call itself "all set".
        /// </summary>
        [TestCase("Ok", false, true)]
        [TestCase("Unknown", false, false)]
        [TestCase("Attention", true, false)]
        [TestCase("Blocked", true, false)]
        public void Step_NeedsAttentionAndIsSatisfiedAreNotOpposites(
            string state,
            bool needsAttention,
            bool isSatisfied)
        {
            bSetupStep step = Step("s", State(state));

            Assert.That(step.NeedsAttention, Is.EqualTo(needsAttention));
            Assert.That(step.IsSatisfied, Is.EqualTo(isSatisfied));
        }

        [Test]
        public void CountUnchecked_CountsEveryStepThatCouldNotBeRead()
        {
            var groups = new[]
            {
                Group("a", Step("one", bStepState.Ok), Step("two", bStepState.Unknown)),
                Group("b", Step("three", bStepState.Unknown), Step("four", bStepState.Blocked)),
            };

            Assert.That(bSetupModel.CountUnchecked(groups), Is.EqualTo(2));
            Assert.That(bSetupModel.CountUnchecked(null), Is.EqualTo(0));
        }

        [Test]
        public void Group_WorstStateWins()
        {
            bSetupGroup group = Group(
                "a",
                Step("one", bStepState.Ok),
                Step("two", bStepState.Blocked),
                Step("three", bStepState.Attention));

            Assert.That(group.WorstState, Is.EqualTo(bStepState.Blocked));
        }

        [Test]
        public void WorstState_SpansEveryGroup()
        {
            var groups = new[]
            {
                Group("a", Step("one", bStepState.Ok)),
                Group("b", Step("two", bStepState.Attention)),
            };

            Assert.That(bSetupModel.WorstState(groups), Is.EqualTo(bStepState.Attention));
        }

        [Test]
        public void Build_WithoutAnAvatarStep_LeavesTheAvatarGroupEmpty()
        {
            IReadOnlyList<bSetupGroup> groups = Build(Companion(bCompanionStatus.ReadyRunning), Environment());

            Assert.That(groups[1].Steps, Is.Empty);
            Assert.That(groups[1].IsClean, Is.True);
        }

        // ------------------------------------------------------------------ helpers

        private static IReadOnlyList<bSetupGroup> Build(
            bCompanionStatusResult companion,
            bEnvironment environment)
            => bSetupModel.Build(companion, environment, null, new bSetupActions());

        private static bCompanionStatusResult Companion(
            bCompanionStatus status,
            string detectedVersion = "2.4.0",
            string productName = bCompanionRequirements.ProductName,
            bool isRunning = false,
            bCompanionBuildLineage lineage = bCompanionBuildLineage.Supported)
            => new bCompanionStatusResult(
                status,
                Required,
                @"C:\bHapticsOSC\bHapticsOSC.exe",
                detectedVersion,
                productName,
                isRunning,
                lineage,
                null,
                "bHapticsOSC");

        private static bEnvironment Environment(
            bProbeState playerInstalled = bProbeState.Yes,
            bProbeState playerRunning = bProbeState.Yes,
            string playerVersion = "2.1.0",
            bProbeState oscEnabled = bProbeState.Yes,
            DateTime oscConfigWritten = default,
            string hapticAvatarName = null)
            => new bEnvironment(
                playerInstalled,
                playerRunning,
                playerVersion,
                oscEnabled,
                oscConfigWritten,
                hapticAvatarName);

        private static bStepState State(string name)
            => (bStepState)Enum.Parse(typeof(bStepState), name);

        private static bProbeState Probe(string name)
            => (bProbeState)Enum.Parse(typeof(bProbeState), name);

        private static bCompanionStatus Status(string name)
            => (bCompanionStatus)Enum.Parse(typeof(bCompanionStatus), name);

        private static bSetupStep Step(string id, bStepState state)
            => new bSetupStep(id, id, state, "value", "detail", "explanation");

        private static bSetupGroup Group(string id, params bSetupStep[] steps)
            => new bSetupGroup(id, id, steps);

        private static bool HasStep(bSetupGroup group, string id)
        {
            foreach (bSetupStep step in group.Steps)
            {
                if (step.Id == id)
                    return true;
            }

            return false;
        }

        private static bSetupStep FindStep(bSetupGroup group, string id)
        {
            foreach (bSetupStep step in group.Steps)
            {
                if (step.Id == id)
                    return step;
            }

            Assert.Fail("No step '" + id + "' in group '" + group.Id + "'.");
            return default;
        }

        /// <summary>
        /// Off Windows the probes cannot run, so the PC group collapses to a single combined row
        /// and the per-probe assertions have nothing to describe.
        /// </summary>
        private static bool IsWindowsEditor(bSetupGroup pc) => HasStep(pc, bSetupModel.StepOsc);
    }
}
