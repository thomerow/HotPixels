using System.Diagnostics;

namespace HotPixels.Tests;

/// <summary>
/// The result of one HotPixels run.
/// </summary>
public sealed record RunResult(int ExitCode, string StdOut, string StdErr) {

   /// <summary>Both streams joined, for assertions that do not care which one a message went to.</summary>
   public string Output => StdOut + StdErr;

   /// <summary>
   /// Returns the value the tool echoed for one of its settings, e.g. "Gamma" -> "0.75".
   /// </summary>
   public string? Setting(string label) {
      foreach (string line in StdOut.Split('\n')) {
         string trimmed = line.Trim();
         if (trimmed.StartsWith(label + ":", StringComparison.Ordinal)) {
            return trimmed[(label.Length + 1)..].Trim();
         }
      }
      return null;
   }
}

/// <summary>
/// Runs the HotPixels executable as a child process.
/// </summary>
/// <remarks>
/// The tests drive the real executable rather than calling into the code, for two reasons: Program keeps
/// its settings in static fields, which would leak between in-process test cases, and Main calls
/// Environment.Exit, which would tear down the test host. A child process per case avoids both, and it
/// tests exactly the contract a user sees.
/// </remarks>
public static class HotPixelsRunner {

   private static readonly string s_executable = LocateExecutable();

   /// <summary>
   /// Runs HotPixels with the given arguments and captures its output.
   /// </summary>
   public static RunResult Run(params string[] args) {
      var startInfo = new ProcessStartInfo {
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
      };

      // The apphost is a native launcher, so it is started directly; the managed dll needs "dotnet"
      if (s_executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) {
         startInfo.FileName = "dotnet";
         startInfo.ArgumentList.Add(s_executable);
      }
      else {
         startInfo.FileName = s_executable;
      }

      foreach (string arg in args) startInfo.ArgumentList.Add(arg);

      using Process process = Process.Start(startInfo)
         ?? throw new InvalidOperationException($"Could not start \"{s_executable}\".");

      // Read both streams before waiting, or a full pipe buffer would deadlock the child
      Task<string> stdout = process.StandardOutput.ReadToEndAsync();
      Task<string> stderr = process.StandardError.ReadToEndAsync();
      process.WaitForExit();

      return new RunResult(process.ExitCode, stdout.Result, stderr.Result);
    }

   /// <summary>
   /// Finds the HotPixels executable next to the test assembly, where the project reference puts it.
   /// </summary>
   private static string LocateExecutable() {
      string directory = AppContext.BaseDirectory;
      string appHost = Path.Combine(directory, OperatingSystem.IsWindows() ? "HotPixels.exe" : "HotPixels");
      if (File.Exists(appHost)) return appHost;

      string dll = Path.Combine(directory, "HotPixels.dll");
      if (File.Exists(dll)) return dll;

      throw new FileNotFoundException(
         $"Neither the HotPixels apphost nor HotPixels.dll was found in \"{directory}\". " +
         "Build the solution so that the project reference copies them next to the tests."
      );
   }
}
