using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace HotPixels.Printing;

/// <summary>
/// Sends raw ESC/POS byte data to a printer, picking the transport from the shape of the target.
/// </summary>
public static class RawPrinter {

   /// <summary>The port ESC/POS network printers listen on ("JetDirect" / raw printing).</summary>
   public const int DefaultNetworkPort = 9100;

   /// <summary>
   /// How a target string is to be reached.
   /// </summary>
   private enum Transport {
      /// <summary>A device node or file, written to directly.</summary>
      Stream,
      /// <summary>A TCP socket, for network printers.</summary>
      Network,
      /// <summary>A print queue managed by the operating system.</summary>
      Queue,
   }

   /// <summary>
   /// Sends a byte array as raw data to the given target.
   /// </summary>
   /// <param name="target">A queue name, a device path, or "host:port".</param>
   /// <param name="bytes">The ESC/POS data to send.</param>
   public static void SendBytes(string target, byte[] bytes) {
      if (bytes is null or { Length: 0 }) return;

      switch (ResolveTransport(target, out string host, out int port)) {
         case Transport.Stream:
            // Works for /dev/usb/lp0 and /dev/lp0 as well as for an ordinary file. FileMode.Create
            // truncates, so writing to a plain file twice does not leave a longer previous run behind;
            // on a character device truncation is a no-op.
            using (var stream = new FileStream(target, FileMode.Create, FileAccess.Write)) {
               stream.Write(bytes, 0, bytes.Length);
            }
            break;

         case Transport.Network:
            using (var client = new TcpClient(host, port))
            using (NetworkStream stream = client.GetStream()) {
               stream.Write(bytes, 0, bytes.Length);
               stream.Flush();
            }
            break;

         default:
            if (OperatingSystem.IsWindows()) WindowsSpoolerPrinter.SendBytes(target, bytes);
            else UnixPrinter.SendBytes(target, bytes);
            break;
      }
   }

   /// <summary>
   /// Returns the print targets available on this machine.
   /// </summary>
   public static IEnumerable<string> ListTargets() =>
      OperatingSystem.IsWindows() ? WindowsSpoolerPrinter.ListPrinters() : UnixPrinter.ListTargets();

   /// <summary>
   /// Decides how a target string is to be reached.
   /// </summary>
   /// <remarks>
   /// The rules are deliberately simple and order-dependent:
   /// <list type="number">
   /// <item>"host:port" with exactly one colon and an all-digit port is a network printer.
   /// A Windows path like C:\out.bin is not matched, because "\out.bin" is not a number.
   /// IPv6 literals are not supported.</item>
   /// <item>A fully qualified path that is not a UNC path is written to directly. This covers
   /// /dev/usb/lp0 on Linux and C:\out.bin on Windows, while leaving \\server\printer to the
   /// spooler, where it belongs.</item>
   /// <item>Anything else is a queue name for the operating system's print system.</item>
   /// </list>
   /// </remarks>
   private static Transport ResolveTransport(string target, out string host, out int port) {
      host = null;
      port = DefaultNetworkPort;

      string[] parts = target.Split(':');
      if (parts.Length == 2 && parts[0].Length > 0 && int.TryParse(parts[1], out int parsedPort)) {
         host = parts[0];
         port = parsedPort;
         return Transport.Network;
      }

      // A Unix-style absolute path is recognised on every platform, so that the same rules hold no
      // matter where the tool runs. Path.IsPathFullyQualified alone would not do: on Windows it rejects
      // "/dev/usb/lp0" for lacking a drive letter, which would send it to the spooler instead.
      if (target.StartsWith('/')) return Transport.Stream;

      bool isUnc = target.StartsWith(@"\\", StringComparison.Ordinal);
      if (!isUnc && Path.IsPathFullyQualified(target)) return Transport.Stream;

      return Transport.Queue;
   }
}
