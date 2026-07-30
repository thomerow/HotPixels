using System.Security.Cryptography;

namespace HotPixels.Tests;

/// <summary>
/// Covers the generated ESC/POS byte stream.
/// </summary>
/// <remarks>
/// Two kinds of assertion are mixed here on purpose. The structural ones (length, header fields, row
/// count) are derived from the ESC/POS specification and the image geometry, so they say what the output
/// *should* be. The hashes are characterisation tests: they only pin down what the pipeline produces
/// today, and their job is to make an accidental change to scaling, gamma, dithering or bit packing fail
/// loudly. If you deliberately change any of those, expect them to fail and update them on purpose.
/// </remarks>
[TestFixture]
public sealed class EscPosOutputTests {

   // ESC @ plus the eight bytes of GS v 0, then four line feeds or the six byte cut sequence
   private const int HeaderLength = 2 + 8;
   private const int FeedTrailerLength = 4;
   private const int CutTrailerLength = 6;

   /// <summary>Height the tool scales the test image to, for a given print width.</summary>
   private static int ExpectedRows(int widthDots) =>
      (int) Math.Round(TestWorkspace.ImageHeight * ((float) widthDots / TestWorkspace.ImageWidth));

   private static byte[] Print(string name, params string[] options) {
      string target = TestWorkspace.OutputPath(name + ".bin");
      RunResult result = HotPixelsRunner.Run([target, TestWorkspace.TestImage, .. options]);

      Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
      return File.ReadAllBytes(target);
   }

   [TestCase(384)]
   [TestCase(512)]
   public void Output_HasTheLengthTheGeometryImplies(int widthDots) {
      byte[] data = Print($"len{widthDots}", "--width=" + widthDots);

      int rows = ExpectedRows(widthDots);
      int expected = HeaderLength + (widthDots / 8 * rows) + FeedTrailerLength;

      Assert.That(data, Has.Length.EqualTo(expected));
   }

   [Test]
   public void Output_StartsWithInitialiseAndTheRasterHeader() {
      const int widthDots = 512;
      byte[] data = Print("header", "--width=" + widthDots);

      int rows = ExpectedRows(widthDots);
      int bytesPerRow = widthDots / 8;

      Assert.Multiple(() => {
         Assert.That(data[0], Is.EqualTo(0x1B), "ESC");
         Assert.That(data[1], Is.EqualTo(0x40), "@ (initialise)");
         Assert.That(data[2], Is.EqualTo(0x1D), "GS");
         Assert.That(data[3], Is.EqualTo(0x76), "v");
         Assert.That(data[4], Is.EqualTo(0x30), "0");
         Assert.That(data[5], Is.EqualTo(0x00), "m = normal, no scaling");
         Assert.That(data[6] | (data[7] << 8), Is.EqualTo(bytesPerRow), "xL/xH: width in bytes");
         Assert.That(data[8] | (data[9] << 8), Is.EqualTo(rows), "yL/yH: height in dot rows");
      });
   }

   [Test]
   public void WithoutCut_OutputEndsWithLineFeeds() {
      byte[] data = Print("feed");

      Assert.That(data[^4..], Is.EqualTo(new byte[] { 0x0A, 0x0A, 0x0A, 0x0A }));
   }

   /// <summary>ESC d 6 advances the paper past the blade, then GS V 0 performs a full cut.</summary>
   [Test]
   public void WithCut_OutputEndsWithFeedAndCut() {
      byte[] data = Print("cut", "--cut");

      Assert.That(data[^6..], Is.EqualTo(new byte[] { 0x1B, 0x64, 0x06, 0x1D, 0x56, 0x00 }));
   }

   /// <summary>
   /// The image and the trailer have to arrive as one write. Sending them separately made the file
   /// transport reopen the target and overwrite the start of the image, and would let a print queue
   /// reorder or delay the cut.
   /// </summary>
   [Test]
   public void ImageAndTrailer_AreWrittenInOnePiece() {
      const int widthDots = 384;
      byte[] data = Print("onepiece", "--width=" + widthDots, "--cut");

      int expected = HeaderLength + (widthDots / 8 * ExpectedRows(widthDots)) + CutTrailerLength;

      Assert.Multiple(() => {
         Assert.That(data, Has.Length.EqualTo(expected));
         Assert.That(data[0], Is.EqualTo(0x1B), "the start of the image must not have been overwritten");
         Assert.That(data[1], Is.EqualTo(0x40));
      });
   }

   /// <summary>
   /// The raster payload for a fixed image and fixed settings. Locks down the whole pipeline: scaling,
   /// gamma, the brightness formula, the dither kernels and the bit packing. Because these hashes hold
   /// on Windows and on Linux, they also prove the two platforms produce identical output.
   /// </summary>
   [TestCase("FloydSteinberg", "72ae4d5116d81bc0e0fc8d04397cddb632384cd996c338172f86086e3e867457")]
   [TestCase("Jarvis", "abbf204f2920b5d9fa2f862cfe4014fd102e2efa61e342e194425f692ec3bb08")]
   [TestCase("Atkinson", "bdde3dae3bc2b2a1179748c6d41d54f403d30431150cbf162453bfbb937efd22")]
   [TestCase("Bayer8x8", "e8a7c18de34e57ae7af2c945eabeebf0b88a5fa93d9e45f4afe6cc899130c4d2")]
   public void RasterPayload_MatchesTheRecordedHash(string ditherMode, string expectedSha256) {
      byte[] data = Print("golden" + ditherMode, "--width=512", "--dither=" + ditherMode);

      byte[] payload = data[HeaderLength..^FeedTrailerLength];
      string actual = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

      Assert.That(actual, Is.EqualTo(expectedSha256),
                  $"the {ditherMode} output changed; update the hash only if the change was intended");
   }
}
