# HotPixels tests

Automated tests for the HotPixels command line tool, written with **NUnit**.

## Running them

From the repository root:

```bash
dotnet test
```

That builds `HotPixels` and the test project and runs everything. A full run takes roughly 20 seconds,
because most tests start the tool as a child process.

Useful variations:

```bash
dotnet test --filter CommandLineTests            # one fixture
dotnet test --filter RasterPayload               # one test by name
dotnet test -v n                                 # list every test as it runs
```

**No printer is involved.** Every test either prints to a file in a temporary directory or points the
tool at a deliberately unreachable target. Nothing is ever sent to a real print queue.

## How the tests work

They drive the **built executable as a child process** and assert on its exit code, its output and the
bytes it writes, rather than calling into the code directly. Two things make that the practical choice:

- `Program` keeps its settings in `static` fields, so in-process test cases would leak state into each
  other and results would depend on execution order.
- `Main` calls `Environment.Exit`, which would tear down the test host.

A process per test case avoids both, needs no changes to the production code, and tests exactly the
contract a user sees. The trade-off is speed and slightly coarser failure messages.

`HotPixelsRunner` finds the executable next to the test assembly, where the project reference puts it,
and falls back to `dotnet HotPixels.dll` if the native launcher is missing.

## What is covered

### `CommandLineTests`

The command line surface.

- **Accepted invocations** — the defaults; `--gamma=0.6` and `--gamma 0.6` being equivalent; `--dither`
  taking a mode name or a number in any casing; `--cut` as a bare flag and with an explicit value;
  options placed before the positional arguments; a repeated option where the last one wins.
- **Rejected invocations** — every malformed value, a missing option value, an option followed by
  another option, an unknown option, a third positional argument, a missing image path, a blank target
  and an unreadable image. Each must exit with **1**, report on stderr, and — this is asserted
  explicitly — **write nothing**, so that a typo cannot cost a misprint.
- **Usage** — no arguments, `--help`, `-h` and `/?` all print usage and exit **0**, and the dither list
  shows every mode with its number.

### `TargetResolutionTests`

That the first argument is routed to the right transport. None of these need a printer: each target
shape fails in a way that identifies the transport that was chosen.

| Target | Expected transport |
|---|---|
| An absolute path | Written directly as a file |
| `/dev/...` | A path, **on Windows too** — `Path.IsPathFullyQualified` alone would reject it for lacking a drive letter and send it to the spooler |
| `127.0.0.1:9` | A socket |
| A plain name | The Windows spooler, or CUPS |
| `\\server\printer` | The spooler, *not* a file, even though it looks like a path |
| `C:\dir\out.bin` | A file, *not* a socket, even though it contains a colon |

The last two are Windows-only concepts and are skipped elsewhere.

### `EscPosOutputTests`

The generated ESC/POS byte stream. Two different kinds of assertion are mixed here deliberately:

**Structural** assertions are derived from the ESC/POS specification and the image geometry, so they say
what the output *should* be: the total length implied by width and row count, the `ESC @` and
`GS v 0 m xL xH yL yH` header fields, the line-feed and `ESC d 6` + `GS V 0` cut trailers, and that the
image and its trailer arrive as a single write.

**Golden hashes** are characterisation tests. They pin down what the pipeline produces *today* — scaling,
gamma, the brightness formula, the dither kernels and the bit packing all at once — so that an accidental
change fails loudly.

> ### Updating a golden hash
>
> If you change scaling, gamma, a dither kernel or the bit packing on purpose, these tests **will** fail.
> That is their job. Check that the new output is what you intended, then take the `But was:` value from
> the failure message and paste it into the `[TestCase]` attribute in `EscPosOutputTests.cs`.
>
> Never update a hash without looking at why it changed.

## Cross-platform

The suite passes unchanged on Windows and on Linux, and the golden hashes are the same on both. That is
the point of them: identical hashes prove the two platforms produce byte-identical ESC/POS output. If you
have WSL, you can check it directly:

```bash
wsl -e bash -c 'cd /path/to/HotPixels && dotnet test'
```

## Test data

The test image is **generated at run time**, not committed, so the repository stays free of binaries and
the input is identical on every machine. It is a 300 × 1000 portrait image combining a vertical gradient,
a solid black block and one-dot horizontal lines — the gradient exercises dithering, the black block
exercises full-black packing, and the thin lines make any vertical scaling error obvious.

Everything is written to a per-run temporary directory that is removed afterwards.
