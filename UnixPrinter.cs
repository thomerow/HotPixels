using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HotPixels.Printing;

/// <summary>
/// Sends raw ESC/POS byte data to a CUPS queue, and enumerates the print targets available on a
/// Unix-like system.
/// </summary>
/// <remarks>
/// Writing to a device node such as /dev/usb/lp0 does not go through here — that is an ordinary file
/// write and is handled by <see cref="RawPrinter"/> on every platform.
/// </remarks>
internal static class UnixPrinter {

   /// <summary>
   /// Sends a byte array to a CUPS queue by piping it through "lp".
   /// </summary>
   /// <remarks>
   /// The "-o raw" option is essential: without it CUPS runs the data through its filter chain, which
   /// mangles ESC/POS control codes. It is the direct equivalent of the "RAW" data type used by the
   /// Windows spooler.
   /// </remarks>
   public static void SendBytes(string queueName, byte[] bytes) {
      var startInfo = new ProcessStartInfo("lp") {
         RedirectStandardInput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
      };
      startInfo.ArgumentList.Add("-d");
      startInfo.ArgumentList.Add(queueName);
      startInfo.ArgumentList.Add("-o");
      startInfo.ArgumentList.Add("raw");

      Process process;
      try {
         process = Process.Start(startInfo);
      }
      catch (Exception ex) {
         throw new InvalidOperationException(
            $"Could not run \"lp\" to print to queue '{queueName}'. Is CUPS installed? " +
            "Alternatively give a device path such as /dev/usb/lp0 as the target.", ex
         );
      }

      using (process) {
         using (Stream stdin = process.StandardInput.BaseStream) {
            stdin.Write(bytes, 0, bytes.Length);
         }

         string errors = process.StandardError.ReadToEnd();
         process.WaitForExit();

         if (process.ExitCode != 0) {
            throw new InvalidOperationException(
               $"\"lp\" failed for queue '{queueName}' with exit code {process.ExitCode}. {errors}".TrimEnd()
            );
         }
      }
   }

   /// <summary>
   /// Returns the print targets available on this machine: the configured CUPS queues plus any printer
   /// device nodes that are present.
   /// </summary>
   public static IEnumerable<string> ListTargets() {
      foreach (string queue in ListCupsQueues()) yield return queue;

      // usblp exposes USB printers here, and parallel ports appear as /dev/lp*
      foreach ((string directory, string pattern) in new[] { ("/dev/usb", "lp*"), ("/dev", "lp*") }) {
         string[] found;
         try {
            found = Directory.Exists(directory) ? Directory.GetFiles(directory, pattern) : [];
         }
         catch (IOException) {
            continue;
         }
         catch (UnauthorizedAccessException) {
            continue;
         }

         foreach (string device in found) yield return device;
      }
   }

   /// <summary>
   /// Returns the CUPS queue names reported by "lpstat -a", or nothing if CUPS is not installed.
   /// </summary>
   private static IEnumerable<string> ListCupsQueues() {
      string output;
      try {
         var startInfo = new ProcessStartInfo("lpstat") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
         };
         startInfo.ArgumentList.Add("-a");

         using Process process = Process.Start(startInfo);
         output = process.StandardOutput.ReadToEnd();
         process.WaitForExit();
      }
      catch {
         // No CUPS on this machine; device paths are still listed by the caller
         yield break;
      }

      // Each line looks like "queuename accepting requests since ..."
      foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
         string name = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
         if (name.Length > 0) yield return name;
      }
   }
}
