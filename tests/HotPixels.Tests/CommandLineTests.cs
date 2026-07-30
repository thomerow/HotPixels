using System.Runtime.CompilerServices;

namespace HotPixels.Tests;

/// <summary>
/// Covers the command line surface: which settings a given invocation produces, and that a malformed
/// one is rejected before anything is printed.
/// </summary>
[TestFixture]
public sealed class CommandLineTests {

   private string Target([CallerMemberName] string name = "") => TestWorkspace.OutputPath(name + ".bin");

   // ---------------------------------------------------------------- accepted invocations

   [Test]
   public void NoOptions_UsesTheDocumentedDefaults() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.Multiple(() => {
         Assert.That(result.Setting("Dither mode"), Is.EqualTo("Jarvis"));
         Assert.That(result.Setting("Gamma"), Is.EqualTo("0.75"));
         Assert.That(result.Setting("Print width"), Is.EqualTo("384 dots"));
         Assert.That(result.Setting("Auto-cut"), Is.EqualTo("off"));
      });
   }

   [Test]
   public void InlineAndSpacedForms_AreEquivalent() {
      RunResult inline = HotPixelsRunner.Run(Target("inline"), TestWorkspace.TestImage, "--gamma=0.6", "--width=512");
      RunResult spaced = HotPixelsRunner.Run(Target("spaced"), TestWorkspace.TestImage, "--gamma", "0.6", "--width", "512");

      Assert.Multiple(() => {
         Assert.That(inline.ExitCode, Is.EqualTo(0), inline.Output);
         Assert.That(spaced.ExitCode, Is.EqualTo(0), spaced.Output);
         Assert.That(spaced.Setting("Gamma"), Is.EqualTo(inline.Setting("Gamma")));
         Assert.That(spaced.Setting("Print width"), Is.EqualTo(inline.Setting("Print width")));
      });
      Assert.That(inline.Setting("Gamma"), Is.EqualTo("0.6"));
   }

   /// <summary>--dither takes the mode name as well as its number, in any casing.</summary>
   [TestCase("Stucki", "Stucki")]
   [TestCase("stucki", "Stucki")]
   [TestCase("3", "Stucki")]
   [TestCase("BAYER8X8", "Bayer8x8")]
   [TestCase("10", "Halftone4x4")]
   public void DitherMode_AcceptsNameAndNumber(string value, string expected) {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage, "--dither=" + value);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.That(result.Setting("Dither mode"), Is.EqualTo(expected));
   }

   /// <summary>A bare --cut means on; an explicit value needs the inline form.</summary>
   [TestCase("", "off")]
   [TestCase("--cut", "on")]
   [TestCase("--cut=1", "on")]
   [TestCase("--cut=0", "off")]
   public void Cut_IsAFlagWithAnOptionalValue(string option, string expected) {
      string[] options = option.Length == 0 ? [] : [option];
      RunResult result = HotPixelsRunner.Run([Target(), TestWorkspace.TestImage, .. options]);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.That(result.Setting("Auto-cut"), Is.EqualTo(expected));
   }

   /// <summary>
   /// --cut must not swallow the argument after it, which is why an explicit value has to be inline.
   /// </summary>
   [Test]
   public void Cut_DoesNotConsumeTheFollowingArgument() {
      RunResult result = HotPixelsRunner.Run("--cut", Target(), TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.That(result.Setting("Auto-cut"), Is.EqualTo("on"));
   }

   [Test]
   public void Options_MayPrecedeThePositionalArguments() {
      RunResult result = HotPixelsRunner.Run("--width", "512", Target(), TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.That(result.Setting("Print width"), Is.EqualTo("512 dots"));
   }

   [Test]
   public void RepeatedOption_LastOneWins() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage, "--width", "384", "--width", "512");

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.That(result.Setting("Print width"), Is.EqualTo("512 dots"));
   }

   // ---------------------------------------------------------------- rejected invocations

   /// <summary>
   /// A malformed value must abort before anything reaches the printer, rather than falling back to a
   /// default and costing a misprint.
   /// </summary>
   [TestCase("--gamma=abc", "--gamma")]
   [TestCase("--gamma=0", "--gamma")]
   [TestCase("--gamma=-1", "--gamma")]
   [TestCase("--width=100", "--width")]
   [TestCase("--width=0", "--width")]
   [TestCase("--dither=nonsense", "--dither")]
   [TestCase("--dither=0", "--dither")]
   [TestCase("--dither=99", "--dither")]
   [TestCase("--cut=xyz", "--cut")]
   public void InvalidValue_FailsWithoutPrinting(string option, string mentioned) {
      string target = Target(option.Replace("=", "_").Replace("-", ""));
      RunResult result = HotPixelsRunner.Run(target, TestWorkspace.TestImage, option);

      Assert.Multiple(() => {
         Assert.That(result.ExitCode, Is.EqualTo(1));
         Assert.That(result.StdErr, Does.StartWith("Error:"));
         Assert.That(result.StdErr, Does.Contain(mentioned));
         Assert.That(File.Exists(target), Is.False, "nothing may be written when the arguments are rejected");
      });
   }

   [Test]
   public void OptionWithoutValue_IsRejected() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage, "--gamma");

      Assert.That(result.ExitCode, Is.EqualTo(1));
      Assert.That(result.StdErr, Does.Contain("--gamma requires a value"));
   }

   /// <summary>A following option is not mistaken for the missing value.</summary>
   [Test]
   public void OptionFollowedByAnotherOption_IsRejected() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage, "--gamma", "--width", "512");

      Assert.That(result.ExitCode, Is.EqualTo(1));
      Assert.That(result.StdErr, Does.Contain("--gamma requires a value"));
   }

   [Test]
   public void UnknownOption_IsRejected() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage, "--nonsense");

      Assert.That(result.ExitCode, Is.EqualTo(1));
      Assert.That(result.StdErr, Does.Contain("unknown option").IgnoreCase);
      Assert.That(result.StdErr, Does.Contain("--nonsense"));
   }

   /// <summary>Only the target and the image path are positional; a third value is a mistake.</summary>
   [Test]
   public void ExtraPositionalArgument_IsRejectedAndExplained() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.TestImage, "3", "0.75");

      Assert.Multiple(() => {
         Assert.That(result.ExitCode, Is.EqualTo(1));
         Assert.That(result.StdErr, Does.Contain("unexpected argument \"3\""));
         Assert.That(result.StdErr, Does.Contain("--dither"), "the message should point at the named options");
      });
   }

   [Test]
   public void MissingImagePath_IsRejected() {
      RunResult result = HotPixelsRunner.Run(Target());

      Assert.That(result.ExitCode, Is.EqualTo(1));
      Assert.That(result.StdErr, Does.Contain("image path"));
   }

   [Test]
   public void BlankTarget_IsRejected() {
      RunResult result = HotPixelsRunner.Run(" ", TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.EqualTo(1));
      Assert.That(result.StdErr, Does.Contain("target is empty"));
   }

   [Test]
   public void UnreadableImage_IsReportedWithExitCodeOne() {
      RunResult result = HotPixelsRunner.Run(Target(), TestWorkspace.OutputPath("no-such-image.png"));

      Assert.That(result.ExitCode, Is.EqualTo(1));
      Assert.That(result.Output, Does.Contain("Could not load image"));
   }

   // ---------------------------------------------------------------- usage

   /// <summary>Asking for help is not an error, so it must exit with 0.</summary>
   [TestCase("")]
   [TestCase("--help")]
   [TestCase("-h")]
   [TestCase("/?")]
   public void Usage_IsPrintedAndSucceeds(string flag) {
      string[] args = flag.Length == 0 ? [] : [flag];
      RunResult result = HotPixelsRunner.Run(args);

      Assert.Multiple(() => {
         Assert.That(result.ExitCode, Is.EqualTo(0));
         Assert.That(result.StdOut, Does.Contain("Usage: HotPixels"));
         Assert.That(result.StdOut, Does.Contain("Available print targets"));
      });
   }

   /// <summary>The dither list is generated from the enum, so every mode must appear with its number.</summary>
   [Test]
   public void Usage_ListsEveryDitherModeWithItsNumber() {
      RunResult result = HotPixelsRunner.Run("--help");

      Assert.Multiple(() => {
         Assert.That(result.StdOut, Does.Contain("1: FloydSteinberg"));
         Assert.That(result.StdOut, Does.Contain("2: Jarvis"));
         Assert.That(result.StdOut, Does.Contain("10: Halftone4x4"));
      });
   }
}
