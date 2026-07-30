using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HotPixels.Printing;

/// <summary>
/// Helper class for sending raw ESC/POS byte data to a printer on Windows.
/// </summary>
public static class RawPrinter {

   #region P/Invoke Declarations

   [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
   static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool ClosePrinter(IntPtr hPrinter);

   [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
   class DOC_INFO_1 {
      public string pDocName;
      public string pOutputFile;
      public string pDatatype;
   }

   [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
   static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 pDocInfo);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool EndDocPrinter(IntPtr hPrinter);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool StartPagePrinter(IntPtr hPrinter);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool EndPagePrinter(IntPtr hPrinter);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

   #endregion P/Invoke Declarations

   /// <summary>
   /// Sends a byte array as raw data to the given printer.
   /// </summary>
   public static void SendBytes(string printerName, byte[] bytes) {
      // Nothing to do for an empty job
      if (bytes is null or { Length: 0 }) return;

      if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero)) {
         throw new InvalidOperationException($"OpenPrinter failed with printer name '{printerName}'.", LastError());
      }

      try {
         var docInfo = new DOC_INFO_1 {
            pDocName = "ESC/POS Raw Job",
            pDatatype = "RAW"
         };

         if (!StartDocPrinter(hPrinter, 1, docInfo)) {
            throw new InvalidOperationException("StartDocPrinter failed.", LastError());
         }
         try {
            if (!StartPagePrinter(hPrinter)) {
               throw new InvalidOperationException("StartPagePrinter failed.", LastError());
            }
            try {
               IntPtr pUnmanagedBytes = Marshal.AllocHGlobal(bytes.Length);
               try {
                  Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                  // WritePrinter is not guaranteed to consume the whole buffer in one call, so keep
                  // writing until everything is gone. Raster image jobs are large (a full-width photo
                  // is easily a few hundred KB), and a silently short write would truncate the bottom
                  // of the image instead of reporting an error.
                  int totalWritten = 0;
                  while (totalWritten < bytes.Length) {
                     if (!WritePrinter(hPrinter, IntPtr.Add(pUnmanagedBytes, totalWritten), bytes.Length - totalWritten, out int written)) {
                        throw new InvalidOperationException(
                           $"WritePrinter failed after {totalWritten} of {bytes.Length} bytes.", LastError()
                        );
                     }
                     // Guard against an endless loop if the driver reports success but consumes nothing
                     if (written <= 0) {
                        throw new InvalidOperationException(
                           $"WritePrinter made no progress at offset {totalWritten} of {bytes.Length} bytes."
                        );
                     }
                     totalWritten += written;
                  }
               }
               finally {
                  Marshal.FreeHGlobal(pUnmanagedBytes);
               }
            }
            finally {
               EndPagePrinter(hPrinter);
            }
         }
         finally {
            EndDocPrinter(hPrinter);
         }
      }
      finally {
         ClosePrinter(hPrinter);
      }
   }

   /// <summary>
   /// Wraps the last Win32 error in an exception so failures carry a diagnosable error code.
   /// Must be called immediately after the failing P/Invoke, before any other one runs.
   /// </summary>
   private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());
}
