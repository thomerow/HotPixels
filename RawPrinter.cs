using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Hilfsklasse zum Senden von Rohdaten an einen Drucker unter Windows.
/// </summary>
public static class RawPrinter {

	#region P/Invoke Deklarationen

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

	#endregion P/Invoke Deklarationen

	/// <summary>
	/// Sendet ein Byte-Array als Rohdaten an den angegebenen Drucker.
	/// </summary>
	public static void SendBytes(string printerName, byte[] bytes) {
		if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero)) {
			throw new InvalidOperationException("OpenPrinter failed.");
		}

		try {
			var docInfo = new DOC_INFO_1 {
				pDocName = "ESC/POS Raw Job",
				pDatatype = "RAW"
			};

			if (!StartDocPrinter(hPrinter, 1, docInfo)) {
				throw new InvalidOperationException("StartDocPrinter failed.");
			}
			try {
				if (!StartPagePrinter(hPrinter)) {
					throw new InvalidOperationException("StartPagePrinter failed.");
				}
				try {
					IntPtr pUnmanagedBytes = Marshal.AllocHGlobal(bytes.Length);
					try {
						Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
						if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out int _)) {
							throw new InvalidOperationException("WritePrinter failed.");
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
}
