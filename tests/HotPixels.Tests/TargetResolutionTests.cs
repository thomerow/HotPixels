namespace HotPixels.Tests;

/// <summary>
/// Covers how the first positional argument is turned into a transport.
/// </summary>
/// <remarks>
/// None of these need a printer. Each target shape fails in a way that identifies the transport that was
/// chosen: a socket target reports a connection failure, a bad path reports a missing directory, and a
/// queue name reaches the platform's print system and is rejected there.
/// </remarks>
[TestFixture]
public sealed class TargetResolutionTests {

   /// <summary>An absolute path is written to directly, on either platform's path syntax.</summary>
   [Test]
   public void AbsolutePath_IsWrittenAsAFile() {
      string target = TestWorkspace.OutputPath("written.bin");
      RunResult result = HotPixelsRunner.Run(target, TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      Assert.That(new FileInfo(target).Length, Is.GreaterThan(0));
   }

   /// <summary>
   /// A unix-style path is recognised on Windows too, so that the documented rules hold everywhere.
   /// Path.IsPathFullyQualified alone would not do that: it rejects "/dev/usb/lp0" for lacking a drive.
   /// </summary>
   [Test]
   public void UnixDevicePath_IsTreatedAsAPathOnEveryPlatform() {
      RunResult result = HotPixelsRunner.Run("/dev/hotpixels-does-not-exist/lp0", TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.Not.EqualTo(0));
      Assert.That(result.Output, Does.Contain("Directory").IgnoreCase.Or.Contain("path").IgnoreCase,
                  "should fail as a file path, not be sent to a print queue");
      Assert.That(result.Output, Does.Not.Contain("OpenPrinter"));
   }

   /// <summary>A "host:port" target goes to a socket. Port 9 is discard, and nothing listens on it here.</summary>
   [Test]
   public void HostAndPort_GoesToASocket() {
      RunResult result = HotPixelsRunner.Run("127.0.0.1:9", TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.Not.EqualTo(0));
      Assert.That(result.Output, Does.Contain("Socket").IgnoreCase.Or.Contain("connection").IgnoreCase,
                  "should fail as a network connection");
   }

   /// <summary>
   /// A plain name is a print queue. It reaches the Windows spooler or CUPS and is rejected there, which
   /// is enough to prove the routing without needing a real queue.
   /// </summary>
   [Test]
   public void PlainName_GoesToThePrintQueue() {
      RunResult result = HotPixelsRunner.Run("HotPixelsNoSuchQueue", TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.Not.EqualTo(0));
      Assert.That(result.Output, Does.Contain(OperatingSystem.IsWindows() ? "OpenPrinter" : "lp"),
                  "should reach the platform's print system");
   }

   /// <summary>
   /// A UNC path is a Windows printer share, so it must stay with the spooler rather than being taken
   /// for a file just because it looks like a path.
   /// </summary>
   [Test]
   [Platform("Win", Reason = "UNC printer shares are a Windows concept")]
   public void UncPath_StaysWithTheSpooler() {
      RunResult result = HotPixelsRunner.Run(@"\\hotpixels-no-such-server\printer", TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.Not.EqualTo(0));
      Assert.That(result.Output, Does.Contain("OpenPrinter"));
   }

   /// <summary>
   /// A Windows drive path contains a colon but is not "host:port", because the part after the colon is
   /// not a number. It must not be mistaken for a network target.
   /// </summary>
   [Test]
   [Platform("Win", Reason = "drive letters only exist on Windows")]
   public void DrivePathWithColon_IsNotMistakenForAHostAndPort() {
      RunResult result = HotPixelsRunner.Run(@"C:\hotpixels-no-such-directory\out.bin", TestWorkspace.TestImage);

      Assert.That(result.ExitCode, Is.Not.EqualTo(0));
      Assert.That(result.Output, Does.Not.Contain("Socket").IgnoreCase);
      Assert.That(result.Output, Does.Contain("Directory").IgnoreCase.Or.Contain("path").IgnoreCase);
   }
}
